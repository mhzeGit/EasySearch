using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace EasySearch;

/// <summary>
/// The frosted-glass background. Each time the palette opens, the screen behind it is captured
/// at a fraction of its size, blurred, and toned for the theme; the window then paints that
/// under the (transparent) web interface.
///
/// Windows' own blur (acrylic) is not used because it turns into a flat colour whenever
/// "Transparency effects" is switched off, on battery saver, and in a few other situations.
/// </summary>
sealed class Backdrop : IDisposable
{
    const int Shrink = 8;          // capture at 1/8 size: cheap, and most of the blur for free
    const int BlurRadius = 4;      // in shrunken pixels
    const int BlurPasses = 3;      // three box blurs approximate a Gaussian
    const float Saturation = 1.75f;

    int[]? _pixels;                // blurred, untoned
    int _width, _height;
    Rectangle _area;               // the part of the screen the pixels cover
    Bitmap? _frame;                // toned and scaled up for the current window position
    (Rectangle Window, bool Dark) _frameKey;

    public bool HasImage => _pixels is not null;

    /// <summary>Captures <paramref name="area"/> of the screen. Call while the palette is hidden.</summary>
    public void Capture(Rectangle area)
    {
        int width = Math.Max(8, area.Width / Shrink), height = Math.Max(8, area.Height / Shrink);
        try
        {
            using var small = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            using (var graphics = Graphics.FromImage(small))
            {
                IntPtr target = graphics.GetHdc();
                IntPtr screen = GetDC(IntPtr.Zero);
                try
                {
                    SetStretchBltMode(target, HALFTONE); // averages pixels while shrinking
                    StretchBlt(target, 0, 0, width, height, screen, area.X, area.Y, area.Width, area.Height, SRCCOPY);
                }
                finally
                {
                    ReleaseDC(IntPtr.Zero, screen);
                    graphics.ReleaseHdc(target);
                }
            }

            var pixels = new int[width * height];
            var data = small.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
            finally { small.UnlockBits(data); }

            var scratch = new int[pixels.Length];
            for (int pass = 0; pass < BlurPasses; pass++)
            {
                BoxBlur(pixels, scratch, width, height, horizontal: true);
                BoxBlur(scratch, pixels, width, height, horizontal: false);
            }

            _pixels = pixels;
            _width = width;
            _height = height;
            _area = area;
        }
        catch (Exception e) when (e is ExternalException or ArgumentException)
        {
            _pixels = null; // secure desktop, remote session without a display, ...: fall back to the plain colour
        }
        _frame?.Dispose();
        _frame = null;
    }

    /// <summary>Paints the background for a window at <paramref name="window"/> (screen coordinates).</summary>
    public void Paint(Graphics graphics, Rectangle window, bool dark, Color fallback)
    {
        if (_pixels is null || window.Width <= 0 || window.Height <= 0)
        {
            graphics.Clear(fallback);
            return;
        }

        if (_frame is null || _frameKey != (window, dark))
        {
            _frame?.Dispose();
            _frame = Render(window, dark, fallback);
            _frameKey = (window, dark);
        }
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImageUnscaled(_frame, 0, 0);
    }

    Bitmap Render(Rectangle window, bool dark, Color fallback)
    {
        using var toned = new Bitmap(_width, _height, PixelFormat.Format32bppRgb);
        var data = toned.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try { Marshal.Copy(Tone(_pixels!, dark), 0, data.Scan0, _pixels!.Length); }
        finally { toned.UnlockBits(data); }

        var frame = new Bitmap(window.Width, window.Height, PixelFormat.Format32bppRgb);
        using var graphics = Graphics.FromImage(frame);
        graphics.Clear(fallback); // shows where the window hangs off the captured screen
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY); // no dark fringe at the screen edges

        var destination = new Rectangle(_area.X - window.X, _area.Y - window.Y, _area.Width, _area.Height);
        graphics.DrawImage(toned, destination, 0, 0, _width, _height, GraphicsUnit.Pixel, attributes);
        return frame;
    }

    /// <summary>
    /// Boosts colour and squeezes brightness into a band that suits the theme, so text stays
    /// readable whether the palette sits over a white document or a dark wallpaper.
    /// </summary>
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
            result[i] = unchecked((int)0xFF000000) | (Clamp(r) << 16) | (Clamp(g) << 8) | Clamp(b);
        }
        return result;

        static int Clamp(float value) => value < 0 ? 0 : value > 255 ? 255 : (int)value;
    }

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

    public void Dispose() => _frame?.Dispose();

    const int HALFTONE = 4;
    const int SRCCOPY = 0x00CC0020;

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    static extern int SetStretchBltMode(IntPtr dc, int mode);

    [DllImport("gdi32.dll")]
    static extern bool StretchBlt(IntPtr target, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, int operation);
}
