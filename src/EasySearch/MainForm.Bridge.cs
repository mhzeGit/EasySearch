using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using EasySearch.Core;
using Microsoft.Web.WebView2.Core;

namespace EasySearch;

// Messages between the web interface and the host. Both directions are JSON objects with a "type".
public sealed partial class MainForm
{
    const int PreviewBytes = 12 * 1024;

    static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".csv", ".tsv",
        ".js", ".ts", ".jsx", ".tsx", ".mjs", ".cjs", ".css", ".scss", ".less", ".html", ".htm", ".vue", ".svelte",
        ".py", ".cs", ".java", ".c", ".h", ".cpp", ".hpp", ".go", ".rs", ".rb", ".php", ".swift", ".kt", ".lua",
        ".sh", ".ps1", ".bat", ".cmd", ".sql", ".gitignore", ".env", ".editorconfig", ".csproj", ".sln", ".props",
    };

    void Post(object message)
    {
        if (!_uiReady || IsDisposed) return;
        var json = JsonSerializer.Serialize(message, AppSettings.Json);
        try
        {
            if (InvokeRequired) BeginInvoke(() => Send(json));
            else Send(json);
        }
        catch (InvalidOperationException)
        {
            // Window is closing.
        }

        void Send(string text)
        {
            if (!IsDisposed && _web.CoreWebView2 is { } core) core.PostWebMessageAsJson(text);
        }
    }

    void Toast(string text, bool error = false) => Post(new { type = "toast", text, error });

    void PostStatus()
    {
        var status = _index.Status;
        Post(new
        {
            type = "status",
            status.Indexing,
            status.Items,
            status.Scanned,
            status.Version,
            lastIndexed = status.LastIndexedUtc is { } at ? new DateTimeOffset(at).ToUnixTimeMilliseconds() : (long?)null,
            durationMs = status.LastDurationMs,
        });
    }

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var document = JsonDocument.Parse(e.WebMessageAsJson);
        var message = document.RootElement;
        string Text(string name) => message.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

        switch (Text("type"))
        {
            case "drag":
                // The user pressed the mouse on an empty part of the search bar: let Windows move the window.
                Native.ReleaseCapture();
                Native.PostMessageW(Handle, Native.WM_SYSCOMMAND, Native.SC_DRAGMOVE, IntPtr.Zero);
                break;

            case "painted":
                Trace($"show: interface drawn {_showTimer.Elapsed.TotalMilliseconds:F0} ms after the key press");
                break;

            case "scale":
                SetUiScale(message.GetProperty("value").GetDouble());
                break;

            case "ready":
                _uiReady = true;
                if (message.TryGetProperty("scale", out var scale)) SetUiScale(scale.GetDouble());
                Post(new
                {
                    type = "init",
                    settings = _settings,
                    theme = DarkTheme ? "dark" : "light",
                    backdrop = true,
                    version = Application.ProductVersion.Split('+')[0],
                    places = Places(),
                    hotkeyOk = _hotkeyRegistered,
                });
                PostStatus();
                if (_options.OpenSettings) Post(new { type = "openSettings" });
                if (_options.Query is not null) Post(new { type = "setQuery", query = _options.Query });
                if (_options.Capture is not null) CaptureAndQuit(_options.Capture);
                break;

            case "search":
                Search(message.GetProperty("id").GetInt32(), message.Deserialize<SearchRequest>(AppSettings.Json)!);
                break;

            case "preview":
                Preview(message.GetProperty("id").GetInt32(), Text("path"));
                break;

            case "action":
                RunAction(Text("action"), Text("path"));
                break;

            case "setSettings":
                ApplySettings(message.GetProperty("settings").Deserialize<AppSettings>(AppSettings.Json)!);
                break;

            case "suspendHotkey":
                // While the user records a new shortcut, the current one must not fire.
                if (message.GetProperty("on").GetBoolean()) UnregisterHotkey();
                else RegisterHotkey();
                break;

            case "reindex":
                _index.Rebuild(CreateScope());
                break;

            case "pickFolder":
                PickFolder(Text("purpose"));
                break;

            case "hide":
                if (!_options.Stay) Hide();
                break;

            case "quit":
                Quit();
                break;
        }
    }

    void Search(int id, SearchRequest request)
    {
        _searchCancel?.Cancel();
        var cancel = _searchCancel = new CancellationTokenSource();
        var index = _index.Index;

        Task.Run(() =>
        {
            try
            {
                var result = SearchEngine.Run(index, request, cancel.Token);
                if (!cancel.IsCancellationRequested)
                    Post(new { type = "results", id, result.Total, result.ElapsedMs, result.Items, result.Error });
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer keystroke.
            }
        });
    }

    /// <summary>Sends the first few kilobytes of a text file for the preview pane.</summary>
    void Preview(int id, string path)
    {
        Task.Run(() =>
        {
            string? text = null;
            try
            {
                if (TextExtensions.Contains(Path.GetExtension(path)) && File.Exists(path))
                {
                    var buffer = new byte[PreviewBytes];
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
                    if (Array.IndexOf(buffer, (byte)0, 0, read) < 0)
                    {
                        using var reader = new StreamReader(new MemoryStream(buffer, 0, read), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                        text = reader.ReadToEnd();
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
            {
            }
            Post(new { type = "preview", id, path, text });
        });
    }

    void RunAction(string action, string path)
    {
        if (path.Length == 0) return;
        try
        {
            switch (action)
            {
                case "open":
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    if (!_options.Stay) Hide();
                    break;

                case "reveal":
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                    if (!_options.Stay) Hide();
                    break;

                case "terminal":
                    var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
                    try { Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{folder}\"") { UseShellExecute = true }); }
                    catch (Win32Exception) { Process.Start(new ProcessStartInfo("powershell.exe") { UseShellExecute = true, WorkingDirectory = folder }); }
                    if (!_options.Stay) Hide();
                    break;

                case "properties":
                    var info = new Native.SHELLEXECUTEINFO
                    {
                        cbSize = Marshal.SizeOf<Native.SHELLEXECUTEINFO>(),
                        fMask = 0x0000000C, // SEE_MASK_INVOKEIDLIST
                        lpVerb = "properties",
                        lpFile = path,
                        nShow = 5,
                    };
                    _suppressHide = true;
                    Native.ShellExecuteEx(ref info);
                    BeginInvoke(() => _suppressHide = false);
                    break;

                case "copyPath":
                    Clipboard.SetText(path);
                    Toast("Path copied");
                    break;

                case "copyName":
                    var name = Path.GetFileName(path);
                    Clipboard.SetText(name.Length > 0 ? name : path);
                    Toast("Name copied");
                    break;

                case "copyFile":
                    Clipboard.SetFileDropList(new StringCollection { path });
                    Toast("Copied — paste it anywhere");
                    break;
            }
        }
        catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException or ExternalException)
        {
            Toast(e.Message, error: true);
        }
    }

    void ApplySettings(AppSettings next)
    {
        var previous = _settings;
        next.Roots = Clean(next.Roots);
        next.Excluded = Clean(next.Excluded);
        if (next.Roots.Count == 0) next.Roots = AppSettings.DefaultRoots();
        _settings = next;
        next.Save();

        bool hotkeyOk = _hotkeyRegistered;
        if (next.Hotkey != previous.Hotkey || !_hotkeyRegistered)
        {
            hotkeyOk = RegisterHotkey();
            if (!hotkeyOk && next.Hotkey != previous.Hotkey) Toast($"{next.Hotkey} is already used by another app", error: true);
        }
        if (next.Theme != previous.Theme) ApplyChrome();
        if (next.LaunchAtLogin != previous.LaunchAtLogin) AppSettings.ApplyLaunchAtLogin(next.LaunchAtLogin);

        var scope = CreateScope();
        if (scope.Signature != new IndexScope(previous.Roots, previous.Excluded).Signature) _index.Rebuild(scope);

        Post(new { type = "settings", settings = next, theme = DarkTheme ? "dark" : "light", hotkeyOk });

        static List<string> Clean(List<string>? values) =>
            [.. (values ?? []).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    void PickFolder(string purpose)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = purpose == "exclude" ? "Choose a folder to leave out of the index" : "Choose a folder",
            UseDescriptionForTitle = true,
        };

        _suppressHide = true;
        try
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
                Post(new { type = "folderPicked", purpose, path = dialog.SelectedPath });
        }
        finally
        {
            _suppressHide = false;
            Activate();
            _web.Focus();
        }
    }

    /// <summary>Well-known folders offered as quick search scopes.</summary>
    static object[] Places()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        (string Label, string Path)[] places =
        [
            ("Home", home),
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("Downloads", Path.Combine(home, "Downloads")),
            ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            ("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
        ];
        return [.. places.Where(p => p.Path.Length > 0 && Directory.Exists(p.Path)).Select(p => (object)new { label = p.Label, path = p.Path })];
    }
}
