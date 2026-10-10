using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EasySearch;

/// <summary>
/// The frosted-glass background. Each time the palette opens, the screen behind it is captured
/// at a fraction of its size, blurred, and toned for the theme; the window then paints that
/// under the (transparent) web interface.
///
/// Windows' own blur (acrylic) is not used because it turns into a flat colour whenever
/// "Transparency effects" is switched off, on battery saver, and in a few other situations.
///
/// Everything here is on the path between pressing the hotkey and seeing the palette, so it is
/// written for speed: plain GDI for the capture and the final copy, and a hand-rolled, parallel
/// bilinear upscale in between (GDI+ took ~90 ms for the same work; this takes a few).
/// </summary>
sealed unsafe class Backdrop : IDisposable
{
    const int Shrink = 8;          // capture at 1/8 size: cheap, and most of the blur for free
    const int BlurRadius = 4;      // in shrunken pixels
    const int BlurPasses = 3;      // three box blurs approximate a Gaussian
    const float Saturation = 1.75f;

    int[]? _pixels;                // blurred, untoned
    int _width, _height;
    Rectangle _area;               // the part of the screen the pixels cover

    IntPtr _surfaceDc, _surface, _previousBitmap;
    uint* _surfaceBits;            // the full-size frame that gets copied to the window
    Size _surfaceSize;
    (Rectangle Window, bool Dark, int[]? Pixels) _frameKey;

    /// <summary>Captures <paramref name="area"/> of the screen. May run on a background thread, but not concurrently with <see cref="Paint"/>.</summary>
    public void Capture(Rectangle area)
    {
        int width = Math.Max(8, area.Width / Shrink), height = Math.Max(8, area.Height / Shrink);
        var pixels = new int[width * height];

        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        var header = new BITMAPINFOHEADER { Size = sizeof(BITMAPINFOHEADER), Width = width, Height = -height, Planes = 1, BitCount = 32 };
        IntPtr bitmap = CreateDIBSection(screen, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        bool ok = false;
        if (bitmap != IntPtr.Zero)
        {
            IntPtr previous = SelectObject(memory, bitmap);
            SetStretchBltMode(memory, HALFTONE); // averages pixels while shrinking
            ok = StretchBlt(memory, 0, 0, width, height, screen, area.X, area.Y, area.Width, area.Height, SRCCOPY);
            if (ok) Marshal.Copy(bits, pixels, 0, pixels.Length);
            SelectObject(memory, previous);
            DeleteObject(bitmap);
        }
        DeleteDC(memory);
        ReleaseDC(IntPtr.Zero, screen);

        if (!ok)
        {
            _pixels = null; // secure desktop, remote session without a display, ...: fall back to the plain colour
            return;
        }

        var scratch = new int[pixels.Length];
        for (int pass = 0; pass < BlurPasses; pass++)
        {
            BoxBlur(pixels, scratch, width, height, horizontal: true);
            BoxBlur(scratch, pixels, width, height, horizontal: false);
        }

        _width = width;
        _height = height;
        _area = area;
        _pixels = pixels;
    }

    /// <summary>Paints the background for a window at <paramref name="window"/> (screen coordinates).</summary>
    public void Paint(Graphics graphics, Rectangle window, bool dark, Color fallback)
    {
        var pixels = _pixels;
        if (pixels is null || window.Width <= 0 || window.Height <= 0 || !EnsureSurface(window.Size))
        {
            graphics.Clear(fallback);
            return;
        }

        var key = (window, dark, pixels);
        if (_frameKey != key)
        {
            Upscale(Tone(pixels, dark), window);
            _frameKey = key;
        }

        IntPtr target = graphics.GetHdc();
        try { BitBlt(target, 0, 0, window.Width, window.Height, _surfaceDc, 0, 0, SRCCOPY); }
        finally { graphics.ReleaseHdc(target); }
    }

    /// <summary>Runs the whole pipeline once on a tiny area so the first real use is already compiled.</summary>
    public void WarmUp()
    {
        var area = new Rectangle(0, 0, 64, 64);
        Capture(area);
        if (_pixels is { } pixels && EnsureSurface(area.Size)) Upscale(Tone(pixels, dark: true), area);
        _pixels = null;
        _frameKey = default;
    }

    /// <summary>Stretches the small blurred image over the window with bilinear filtering, one row per task.</summary>
    void Upscale(int[] source, Rectangle window)
    {
        int width = window.Width, height = window.Height, sourceWidth = _width, sourceHeight = _height;
        float scaleX = sourceWidth / (float)_area.Width, scaleY = sourceHeight / (float)_area.Height;
        int lastColumn = Math.Max(0, sourceWidth - 2), lastRow = Math.Max(0, sourceHeight - 2);

        // Where each window column falls in the source: left neighbour and blend weight (0..256).
        var left = new int[width];
        var blend = new uint[width];
        for (int x = 0; x < width; x++)
        {
            float at = Math.Clamp((window.X + x - _area.X + 0.5f) * scaleX - 0.5f, 0, sourceWidth - 1);
            left[x] = Math.Min((int)at, lastColumn);
            blend[x] = (uint)((at - left[x]) * 256);
        }

        nint bits = (nint)_surfaceBits;
        int offsetY = window.Y - _area.Y;
        int step = sourceWidth > 1 ? 1 : 0;
        Parallel.For(0, height, y =>
        {
            float at = Math.Clamp((offsetY + y + 0.5f) * scaleY - 0.5f, 0, sourceHeight - 1);
            int top = Math.Min((int)at, lastRow);
            int upper = top * sourceWidth, lower = Math.Min(top + 1, sourceHeight - 1) * sourceWidth;
            UpscaleRow((uint*)bits + (long)y * width, source, left, blend, upper, lower, step, (uint)((at - top) * 256));
        });
    }

    // These loops run on the way to showing the palette. AggressiveOptimization makes the runtime
    // compile them fully the first time, instead of starting with slow code and improving it later.

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void UpscaleRow(uint* row, int[] source, int[] left, uint[] blend, int upper, int lower, int step, uint weightY)
    {
        for (int x = 0; x < left.Length; x++)
        {
            int l = left[x];
            uint weightX = blend[x];
            uint a = Mix((uint)source[upper + l], (uint)source[upper + l + step], weightX);
            uint b = Mix((uint)source[lower + l], (uint)source[lower + l + step], weightX);
            row[x] = Mix(a, b, weightY) | 0xFF000000;
        }
    }

    /// <summary>Blends two pixels; red and blue share one multiplication, green gets the other.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint Mix(uint a, uint b, uint weight)
    {
        uint keep = 256 - weight;
        uint redBlue = (((a & 0xFF00FF) * keep + (b & 0xFF00FF) * weight) >> 8) & 0xFF00FF;
        uint green = (((a & 0x00FF00) * keep + (b & 0x00FF00) * weight) >> 8) & 0x00FF00;
        return redBlue | green;
    }

    /// <summary>
    /// Boosts colour and squeezes brightness into a band that suits the theme, so text stays
    /// readable whether the palette sits over a white document or a dark wallpaper.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static int[] Tone(int[] source, bool dark)
    {
        float gain = dark ? 0.46f : 0.30f, lift = dark ? 12f : 172f;
        var result = new int[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            int pixel = source[i];
            float r = (pixel >> 16) & 0xFF, g = (pixel >> 8) & 0xFF, b = pixel & 0xFF;
            float luma = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            r = (luma + (r - luma) * Saturation) * gain + lift;
            g = (luma + (g - luma) * Saturation) * gain + lift;
            b = (luma + (b - luma) * Saturation) * gain + lift;
            result[i] = (Clamp(r) << 16) | (Clamp(g) << 8) | Clamp(b);
        }
        return result;

        static int Clamp(float value) => value < 0 ? 0 : value > 255 ? 255 : (int)value;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void BoxBlur(int[] source, int[] target, int width, int height, bool horizontal)
    {
        int lines = horizontal ? height : width, length = horizontal ? width : height;
        int step = horizontal ? 1 : width, lineStep = horizontal ? width : 1;
        const int window = 2 * BlurRadius + 1;

        for (int line = 0; line < lines; line++)
        {
            int start = line * lineStep;
            for (int i = 0; i < length; i++)
            {
                int r = 0, g = 0, b = 0;
                for (int k = -BlurRadius; k <= BlurRadius; k++)
                {
                    int pixel = source[start + Math.Clamp(i + k, 0, length - 1) * step];
                    r += (pixel >> 16) & 0xFF;
                    g += (pixel >> 8) & 0xFF;
                    b += pixel & 0xFF;
                }
                target[start + i * step] = ((r / window) << 16) | ((g / window) << 8) | (b / window);
            }
        }
    }

    /// <summary>A memory bitmap the size of the window whose pixels can be written directly.</summary>
    bool EnsureSurface(Size size)
    {
        if (_surface != IntPtr.Zero && _surfaceSize == size) return true;
        ReleaseSurface();

        IntPtr screen = GetDC(IntPtr.Zero);
        _surfaceDc = CreateCompatibleDC(screen);
        var header = new BITMAPINFOHEADER { Size = sizeof(BITMAPINFOHEADER), Width = size.Width, Height = -size.Height, Planes = 1, BitCount = 32 };
        _surface = CreateDIBSection(screen, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        ReleaseDC(IntPtr.Zero, screen);
        if (_surface == IntPtr.Zero)
        {
            DeleteDC(_surfaceDc);
            _surfaceDc = IntPtr.Zero;
            return false;
        }

        _previousBitmap = SelectObject(_surfaceDc, _surface);
        _surfaceBits = (uint*)bits;
        _surfaceSize = size;
        _frameKey = default;
        return true;
    }

    void ReleaseSurface()
    {
        if (_surface == IntPtr.Zero) return;
        SelectObject(_surfaceDc, _previousBitmap);
        DeleteObject(_surface);
        DeleteDC(_surfaceDc);
        _surface = _surfaceDc = IntPtr.Zero;
        _surfaceBits = null;
    }

    public void Dispose() => ReleaseSurface();

    const int HALFTONE = 4;
    const int SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ColorsUsed, ColorsImportant;
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER header, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    static extern int SetStretchBltMode(IntPtr dc, int mode);

    [DllImport("gdi32.dll")]
    static extern bool StretchBlt(IntPtr target, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, int operation);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int operation);
}
