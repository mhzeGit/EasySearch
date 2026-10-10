using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace EasySearch;

/// <summary>
/// Produces the same icons and thumbnails Explorer shows, as PNG bytes. The shell is asked on
/// dedicated STA threads, because some thumbnail providers only work there.
/// </summary>
sealed class ShellImages : IDisposable
{
    const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;

    static readonly Guid ImageFactoryId = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>File types whose icon depends on the individual file rather than on the extension.</summary>
    static readonly HashSet<string> PerFileIcons = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".lnk", ".ico", ".cur", ".ani", ".url", ".scr", ".msi", ".appref-ms" };


    readonly BlockingCollection<Action> _work = new();
    readonly ConcurrentDictionary<string, byte[]> _iconsByType = new(StringComparer.OrdinalIgnoreCase);

    public ShellImages()
    {
        for (int i = 0; i < 2; i++)
        {
            var thread = new Thread(() =>
            {
                foreach (var job in _work.GetConsumingEnumerable()) job();
            })
            { IsBackground = true, Name = "EasySearch shell images" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }

    /// <summary>
    /// Loads the Windows icon for a file or folder (or, with <paramref name="thumbnail"/>, a preview
    /// of its contents) and calls back with PNG bytes, or null if Windows has nothing to offer.
    /// </summary>
    public void Request(string path, int size, bool thumbnail, bool isDirectory, Action<byte[]?> done)
    {
        size = Math.Clamp(size, 16, 1024);
        string? cacheKey = null;
        if (!thumbnail && !isDirectory)
        {
            var extension = Path.GetExtension(path);
            if (extension.Length > 0 && !PerFileIcons.Contains(extension))
            {
                cacheKey = $"{extension}|{size}";
                if (_iconsByType.TryGetValue(cacheKey, out var cached))
                {
                    done(cached);
                    return;
                }
            }
        }

        _work.Add(() =>
        {
            // If the item itself yields nothing (deleted meanwhile, unreadable, no thumbnail
            // provider), fall back to the standard Windows icon for its type.
            byte[]? png = TryRender(path, size, thumbnail ? SIIGBF_BIGGERSIZEOK : SIIGBF_ICONONLY)
                ?? TryRender(StandIn(path, isDirectory), size, SIIGBF_ICONONLY);
            if (png is not null && cacheKey is not null) _iconsByType[cacheKey] = png;
            done(png);
        });
    }

    static byte[]? TryRender(string? path, int size, int flags)
    {
        if (path is null) return null;
        try
        {
            return Render(path, size, flags);
        }
        catch (Exception)
        {
            // Icon and thumbnail providers are third-party shell extensions and can fail in any way.
            return null;
        }
    }

    /// <summary>
    /// An empty file (or folder) of the same type in the app's cache folder. Asking Windows for
    /// its icon gives the standard icon for that type at any size.
    /// </summary>
    static string? StandIn(string path, bool isDirectory)
    {
        try
        {
            var folder = Path.Combine(AppSettings.CacheFolder, "icons");
            if (isDirectory)
            {
                var standIn = Path.Combine(folder, "folder");
                Directory.CreateDirectory(standIn);
                return standIn;
            }

            var extension = Path.GetExtension(path);
            bool plain = extension.Length is > 1 and <= 24 && extension.AsSpan(1).IndexOfAnyExcept(SafeExtensionChars) < 0;
            var file = Path.Combine(folder, plain ? "type" + extension.ToLowerInvariant() : "type");
            if (!File.Exists(file))
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(file, []);
            }
            return file;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static readonly System.Buffers.SearchValues<char> SafeExtensionChars =
        System.Buffers.SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_");

    static byte[]? Render(string path, int size, int flags)
    {
        int hr = Native.SHCreateItemFromParsingName(path, IntPtr.Zero, ImageFactoryId, out var factory);
        if (hr != 0 || factory is null) return null;
        try
        {
            // The shell answers E_PENDING while it is still extracting an image: ask again shortly.
            const int E_PENDING = unchecked((int)0x8000000A);
            IntPtr bitmap;
            for (int attempt = 0; (hr = factory.GetImage(new Native.SIZE { Width = size, Height = size }, flags, out bitmap)) == E_PENDING && attempt < 20; attempt++)
                Thread.Sleep(15);
            if (hr != 0) return null;
            try { return ToPng(bitmap); }
            finally { Native.DeleteObject(bitmap); }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>The shell hands back premultiplied 32-bit pixels; GDI+ needs to be told so to keep the transparency.</summary>
    static unsafe byte[] ToPng(IntPtr hBitmap)
    {
        using var source = Image.FromHbitmap(hBitmap);
        using var stream = new MemoryStream();

        if (Image.GetPixelFormatSize(source.PixelFormat) == 32)
        {
            var bounds = new Rectangle(0, 0, source.Width, source.Height);
            var data = source.LockBits(bounds, ImageLockMode.ReadOnly, source.PixelFormat);
            try
            {
                // A bottom-up bitmap has a negative stride, with Scan0 pointing at its last row in memory.
                bool hasAlpha = false;
                var first = data.Stride < 0 ? data.Scan0 + data.Stride * (data.Height - 1) : data.Scan0;
                var pixels = new ReadOnlySpan<uint>((void*)first, Math.Abs(data.Stride) / 4 * data.Height);
                foreach (uint pixel in pixels)
                {
                    if (pixel >> 24 != 0) { hasAlpha = true; break; }
                }

                if (hasAlpha)
                {
                    using var withAlpha = new Bitmap(data.Width, data.Height, data.Stride, PixelFormat.Format32bppPArgb, data.Scan0);
                    withAlpha.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }
            finally
            {
                source.UnlockBits(data);
            }
        }

        source.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    public void Dispose() => _work.CompleteAdding();
}
