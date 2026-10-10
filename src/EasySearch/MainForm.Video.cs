using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace EasySearch;

public sealed partial class MainForm
{
    const string VideoHost = "video.easysearch.example";
    readonly SemaphoreSlim _videoReads = new(1, 1);
    VideoSource? _videoSource;

    sealed record VideoSource(int Id, string Path, string Key, CancellationTokenSource Cancel);

    void InitializeVideoPreview(CoreWebView2 core)
    {
        core.AddWebResourceRequestedFilter($"https://{VideoHost}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnVideoRequested;
        core.WebMessageReceived += OnVideoMessage;
        VisibleChanged += (_, _) =>
        {
            if (!Visible) ClearVideoSource();
            Post(new { type = "paletteVisibility", visible = Visible });
        };
        FormClosed += (_, _) => ClearVideoSource();
    }

    void ClearVideoSource()
    {
        var source = _videoSource;
        _videoSource = null;
        if (source is null) return;
        source.Cancel.Cancel();
        source.Cancel.Dispose();
    }

    void OnVideoMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var document = JsonDocument.Parse(e.WebMessageAsJson);
        var message = document.RootElement;
        if (message.GetProperty("type").GetString() == "ready")
            Post(new { type = "paletteVisibility", visible = Visible });
        if (message.GetProperty("type").GetString() != "videoPreview") return;
        int id = message.GetProperty("id").GetInt32();
        string? path = message.GetProperty("path").GetString();
        if (path is null)
        {
            if (_videoSource?.Id == id) ClearVideoSource();
            return;
        }
        ClearVideoSource();
        if (!Visible || !_settings.ShowPreview || !System.IO.Path.IsPathFullyQualified(path)) return;
        var source = _videoSource = new(id, path, Guid.NewGuid().ToString("N"), new());
        Post(new { type = "videoSource", id, url = $"https://{VideoHost}/{source.Key}" });
    }

    async void OnVideoRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (new Uri(e.Request.Uri).Host != VideoHost) return;
        var environment = _web.CoreWebView2.Environment;
        const string headers = "Cache-Control: no-store\r\nAccess-Control-Allow-Origin: https://easysearch.example\r\nAccept-Ranges: bytes";
        var source = _videoSource;
        if (source is null || new Uri(e.Request.Uri).AbsolutePath != "/" + source.Key)
        {
            e.Response = environment.CreateWebResourceResponse(null, 404, "Not Found", headers);
            return;
        }
        var token = source.Cancel.Token;
        string? rangeHeader = e.Request.Headers.Contains("Range") ? e.Request.Headers.GetHeader("Range") : null;
        var deferral = e.GetDeferral();
        try
        {
            // Serialized, cancellable reads keep old selections from queuing disk work or buffers.
            await _videoReads.WaitAsync(token);
            (byte[] Bytes, long Length, VideoByteRange? Range) result;
            try
            {
                result = await Task.Run(async () =>
                {
                    using var file = new FileStream(source.Path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.RandomAccess);
                    long length = file.Length;
                    var range = VideoByteRange.Parse(rangeHeader, length);
                    if (range is null) return (Array.Empty<byte>(), length, range);
                    file.Position = range.Value.Start;
                    var bytes = new byte[range.Value.Count];
                    await file.ReadExactlyAsync(bytes, token);
                    return (bytes, length, range);
                }, token);
            }
            finally { _videoReads.Release(); }
            token.ThrowIfCancellationRequested();
            if (result.Range is not { } requested)
            {
                e.Response = environment.CreateWebResourceResponse(null, 416, "Range Not Satisfiable",
                    headers + $"\r\nContent-Range: bytes */{result.Length}");
                return;
            }
            string mime = System.IO.Path.GetExtension(source.Path).ToLowerInvariant() switch
            {
                ".webm" => "video/webm", ".mkv" => "video/x-matroska", ".mov" => "video/quicktime",
                ".avi" => "video/x-msvideo", ".wmv" => "video/x-ms-wmv", _ => "video/mp4",
            };
            e.Response = environment.CreateWebResourceResponse(new MemoryStream(result.Bytes, writable: false),
                206, "Partial Content", headers + $"\r\nContent-Type: {mime}\r\nContent-Length: {requested.Count}" +
                $"\r\nContent-Range: bytes {requested.Start}-{requested.End}/{result.Length}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (!IsDisposed && !Disposing)
            {
                try { e.Response = environment.CreateWebResourceResponse(null, 404, "Not Found", headers); }
                catch (System.Runtime.InteropServices.COMException) { } // WebView closed during the read.
            }
        }
        catch (System.Runtime.InteropServices.COMException) when (IsDisposed || Disposing) { }
        finally
        {
            try { deferral.Complete(); }
            catch (System.Runtime.InteropServices.COMException) when (IsDisposed || Disposing) { }
        }
    }
}
