using EasySearch.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace EasySearch;

/// <summary>
/// The palette window: a frameless, always-on-top window that hosts the web interface,
/// lives in the tray and is summoned with a global hotkey.
/// </summary>
public sealed partial class MainForm : Form
{
    const int HotkeyId = 0x4553;
    const int ContextHotkeyId = 0x4554;
    const string AppHost = "easysearch.example";
    const string ImageHost = "images.easysearch.example";

    static readonly Size BaseSize = new(920, 600);

    readonly StartupOptions _options;
    readonly WebView2 _web;
    readonly NotifyIcon _tray;
    readonly IndexService _index;
    readonly ShellImages _images = new();
    readonly Backdrop _backdrop = new();
    readonly System.Windows.Forms.Timer _statusTimer;

    AppSettings _settings;
    CancellationTokenSource? _searchCancel;
    bool _startHidden;
    bool _initialized;
    bool _uiReady;
    bool _quitting;
    bool _suppressHide;
    bool _hotkeyRegistered;
    bool _contextHotkeyRegistered;
    HotkeyHook? _hotkeyHook;
    int _statusDirty;
    double _uiScale;
    bool _capturing;
    readonly System.Diagnostics.Stopwatch _showTimer = new();
    readonly System.Windows.Forms.Timer _trimTimer;

    public MainForm(StartupOptions options)
    {
        _options = options;
        _startHidden = options.Background;
        _settings = AppSettings.Load();
        AppSettings.ApplyLaunchAtLogin(_settings.LaunchAtLogin);

        Text = "EasySearch";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle = FormBorderStyle.Sizable; // keeps the native shadow and rounded corners; the frame itself is removed in WndProc
        ControlBox = false;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = SurfaceColor; // only seen if the screen behind could not be captured
        Size = BaseSize;

        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };
        Controls.Add(_web);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open EasySearch", null, (_, _) => ShowPalette());
        menu.Items.Add("Settings…", null, (_, _) => { ShowPalette(); Post(new { type = "openSettings" }); });
        menu.Items.Add("Rebuild index", null, (_, _) => _index!.Rebuild(CreateScope()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        _tray = new NotifyIcon { Icon = Icon, Text = "EasySearch", ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowPalette(); };

        _index = new IndexService(Path.Combine(AppSettings.CacheFolder, "index.bin"));
        _index.Changed += () => Interlocked.Exchange(ref _statusDirty, 1);
        var scope = CreateScope();
        Task.Run(() => _index.Start(scope));

        _statusTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _statusTimer.Tick += (_, _) =>
        {
            if (Interlocked.Exchange(ref _statusDirty, 0) == 1 || _index.Status.Indexing) PostStatus();
        };
        _statusTimer.Start();

        // Twenty seconds after the palette was dismissed, let the web view release memory it is not using.
        _trimTimer = new System.Windows.Forms.Timer { Interval = 20_000 };
        _trimTimer.Tick += (_, _) =>
        {
            _trimTimer.Stop();
            if (!Visible && _web.CoreWebView2 is { } core) core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
        };
        _backdrop.WarmUp();

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    Color SurfaceColor => DarkTheme ? Color.FromArgb(29, 29, 33) : Color.FromArgb(245, 245, 247);

    bool DarkTheme => _settings.Theme switch
    {
        "dark" => true,
        "light" => false,
        _ => AppSettings.SystemUsesDarkTheme(),
    };

    IndexScope CreateScope() => new(_settings.Roots, _settings.Excluded);

    // ---- window ------------------------------------------------------------------------

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW; // keep it out of Alt+Tab
            cp.Style &= ~Native.WS_CLIPCHILDREN;   // the blurred backdrop is painted underneath the web view
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            m.Result = IntPtr.Zero; // the whole window is client area: no title bar, no borders
            return;
        }
        if (m.Msg == Native.WM_HOTKEY && m.WParam == HotkeyId)
        {
            Toggle();
            return;
        }
        if (m.Msg == Native.WM_HOTKEY && m.WParam == ContextHotkeyId)
        {
            ShowWithContext();
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyChrome();
        RegisterHotkey();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotkey();
        base.OnHandleDestroyed(e);
    }

    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden)
        {
            // Launched in the background: create everything, but stay in the tray.
            _startHidden = false;
            if (!IsHandleCreated) CreateHandle();
            value = false;
        }
        bool appearing = value && !Visible;
        Task? capture = null;
        if (appearing)
        {
            if (!IsHandleCreated) CreateHandle();
            _showTimer.Restart();
            _marks.Clear();
            PlaceOnScreen();

            // Cloaked, the window can be shown and laid out without being on screen yet. That lets
            // the screen behind it be photographed on another thread while the window wakes up,
            // and avoids a flash of the previous backdrop.
            Cloak(true);
            var area = Rectangle.Intersect(Rectangle.Inflate(Bounds, 32, 32), Screen.FromRectangle(Bounds).Bounds);
            _capturing = true;
            capture = Task.Run(() => _backdrop.Capture(area));
            Mark("placed");
        }

        base.SetVisibleCore(value);

        if (capture is not null)
        {
            Mark("visible");
            try { capture.Wait(); }
            catch (AggregateException) { } // no backdrop: the plain colour is painted instead
            _capturing = false;
            Mark("captured");
            Invalidate(false);
            Update();
            Mark("painted");
            Cloak(false);
            SetBackgroundMode(false);
            Mark("ready");
        }
        else if (!value)
        {
            SetBackgroundMode(true);
        }
        Initialize();
    }

    void Cloak(bool cloaked)
    {
        int value = cloaked ? 1 : 0;
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_CLOAK, ref value, sizeof(int));
    }

    /// <summary>
    /// While the palette is hidden nothing needs to be drawn or refreshed: status updates stop,
    /// the index folds in changes at a slower pace, and after a little while the web view is
    /// told it may give memory back.
    /// </summary>
    void SetBackgroundMode(bool background)
    {
        _index.Relaxed = background;
        _statusTimer.Enabled = !background;
        _trimTimer.Enabled = background;
        if (!background)
        {
            if (_web.CoreWebView2 is { } core) core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            _index.Refresh(); // fold in the latest file changes right away
            PostStatus();
        }
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        Invalidate(false); // the backdrop is tied to the screen, so it shifts as the window moves
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate(false);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_settings.HideOnBlur && !_suppressHide && !_options.Stay) Hide();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_quitting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true; // Alt+F4 only dismisses the palette
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _statusTimer.Dispose();
        _trimTimer.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _index.Dispose();
        _images.Dispose();
        _backdrop.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (_capturing) return; // still cloaked; the real paint follows as soon as the capture is in
        _backdrop.Paint(e.Graphics, Bounds, DarkTheme, SurfaceColor);
    }

    /// <summary>Set EASYSEARCH_TRACE=1 to log timings to trace.log in the data folder.</summary>
    static readonly bool Tracing = Environment.GetEnvironmentVariable("EASYSEARCH_TRACE") == "1";

    readonly System.Text.StringBuilder _marks = new();

    /// <summary>Notes how far into the current show we are (only when tracing).</summary>
    void Mark(string step)
    {
        if (Tracing) _marks.Append($" {step}@{_showTimer.Elapsed.TotalMilliseconds:F0}");
    }

    internal static void Trace(string line)
    {
        if (!Tracing) return;
        try { File.AppendAllText(Path.Combine(AppSettings.DataFolder, "trace.log"), $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}"); }
        catch (IOException) { }
    }

    void ApplyChrome()
    {
        bool dark = DarkTheme;
        int value = dark ? 1 : 0;
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        value = Native.DWMWCP_ROUND;
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
        value = 1; // no Windows open/close animation: the palette should simply be there
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_TRANSITIONS_FORCEDISABLED, ref value, sizeof(int));
        value = dark ? 0x00484848 : 0x00BDBDBD; // COLORREF
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_BORDER_COLOR, ref value, sizeof(int));

        BackColor = SurfaceColor;
        Invalidate(false); // the backdrop is toned differently for light and dark
    }

    /// <summary>Centers the palette on the monitor the pointer is on, a little above the middle.</summary>
    void PlaceOnScreen()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        double scale = _uiScale > 0 ? _uiScale : DeviceDpi / 96.0;
        var size = new Size(
            Math.Min((int)(BaseSize.Width * scale), area.Width - 24),
            Math.Min((int)(BaseSize.Height * scale), area.Height - 24));
        var bounds = new Rectangle(
            area.Left + (area.Width - size.Width) / 2,
            area.Top + (int)((area.Height - size.Height) * 0.38),
            size.Width, size.Height);
        if (Bounds != bounds) Bounds = bounds;
    }

    /// <summary>
    /// The interface reports how many physical pixels one CSS pixel covers. That includes display
    /// scaling and the Windows text-size setting, so the window grows with the content.
    /// </summary>
    void SetUiScale(double scale)
    {
        if (scale is < 0.5 or > 5 || Math.Abs(scale - _uiScale) < 0.001) return;
        _uiScale = scale;
        if (Visible) PlaceOnScreen();
    }

    /// <summary>Development: with --passive the window never takes keyboard focus, so it can be inspected without interrupting work.</summary>
    protected override bool ShowWithoutActivation => _options.Passive;

    public void ShowPalette() => ShowPalette(context: false, forceNormal: true);

    void ShowWithContext() => ShowPalette(context: true, forceNormal: false);

    void ShowPalette(bool context, bool forceNormal)
    {
        // Looked at before the palette takes focus: this is the window the user was working in.
        var foreground = IsHandleCreated ? Native.GetForegroundWindow() : IntPtr.Zero;
        var folder = !forceNormal && context && foreground != IntPtr.Zero
            ? ProjectFolder.FolderAsync(foreground)
            : !forceNormal && _settings.ScopeToExplorer ? ExplorerFolder.FolderAsync(ExplorerFolder.ForegroundExplorer(Handle)) : null;

        Show(); // positions the window and captures the backdrop when it was hidden
        // Hide-on-blur must not fire while focus is being moved onto the palette.
        _suppressHide = true;
        try { if (!_options.Passive) TakeFocus(); }
        finally { _suppressHide = false; }
        Mark("focused");
        if (!_options.Passive) _web.Focus();
        Mark("web-focused");
        Post(new { type = "shown" });
        if (context) Post(new { type = "contextOpen" });
        if (context || (!forceNormal && _settings.ScopeToExplorer)) PostExplorerFolder(folder);
        if (Tracing) Trace("show steps:" + _marks);
    }

    /// <summary>
    /// Tells the interface which Explorer folder (if any) the palette was opened from. The lookup
    /// runs off the UI thread, so the palette appears at once and the folder follows a moment later.
    /// </summary>
    void PostExplorerFolder(Task<string?>? folder)
    {
        if (folder is null)
        {
            Post(new { type = "explorerFolder", path = (string?)null });
            return;
        }

        folder.ContinueWith(task =>
        {
            string? path = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(() => Post(new { type = "explorerFolder", path }));
            }
            catch (InvalidOperationException)
            {
                // The window went away while the lookup was running.
            }
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Windows only lets a background app come to the front in certain situations. Borrowing the
    /// input state of whichever window currently has focus is the dependable way to do it.
    /// </summary>
    void TakeFocus()
    {
        Activate();
        if (Native.GetForegroundWindow() == Handle) return;

        uint current = Native.GetCurrentThreadId();
        uint foreground = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero);
        bool attached = foreground != 0 && foreground != current && Native.AttachThreadInput(foreground, current, true);
        Native.BringWindowToTop(Handle);
        Native.SetForegroundWindow(Handle);
        if (attached) Native.AttachThreadInput(foreground, current, false);
        if (Native.GetForegroundWindow() == Handle) return;

        // Last resort: a tap of an unassigned key makes this process the one that produced the latest input.
        Native.keybd_event(0xFF, 0, 0, UIntPtr.Zero);
        Native.keybd_event(0xFF, 0, 2, UIntPtr.Zero);
        Native.SetForegroundWindow(Handle);
    }

    void Toggle()
    {
        if (Visible) Hide();
        else ShowPalette();
    }

    void Quit()
    {
        _quitting = true;
        Close();
    }

    void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || _settings.Theme != "system" || !IsHandleCreated) return;
        ApplyChrome();
        Post(new { type = "theme", theme = DarkTheme ? "dark" : "light" });
    }

    // ---- hotkey ------------------------------------------------------------------------

    bool RegisterHotkey()
    {
        UnregisterHotkey();
        Native.RegisterHotKey(Handle, HotkeyId, Native.MOD_WIN | Native.MOD_NOREPEAT, 0x20);
        _hotkeyHook = HotkeyHook.TryInstall(0x20,
            () => BeginInvoke(Toggle), () => BeginInvoke(ShowWithContext));
        _hotkeyRegistered = _hotkeyHook is not null;
        _contextHotkeyRegistered = Native.RegisterHotKey(Handle, ContextHotkeyId,
            Native.MOD_WIN | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x20);
        return _hotkeyRegistered;
    }

    void UnregisterHotkey()
    {
        if (_hotkeyHook is not null) _hotkeyHook.Dispose();
        else if (_hotkeyRegistered) Native.UnregisterHotKey(Handle, HotkeyId);
        if (_contextHotkeyRegistered) Native.UnregisterHotKey(Handle, ContextHotkeyId);
        _hotkeyHook = null;
        _hotkeyRegistered = false;
        _contextHotkeyRegistered = false;
    }

    // ---- web view ----------------------------------------------------------------------

    async void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(AppSettings.CacheFolder, "WebView2"));
            await _web.EnsureCoreWebView2Async(environment);

            var core = _web.CoreWebView2;
            var settings = core.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsPinchZoomEnabled = false;
            settings.IsSwipeNavigationEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
#if DEBUG
            settings.AreDevToolsEnabled = true;
#else
            settings.AreDevToolsEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
#endif

            core.SetVirtualHostNameToFolderMapping(AppHost, Path.Combine(AppContext.BaseDirectory, "ui"),
                CoreWebView2HostResourceAccessKind.Allow);
            core.AddWebResourceRequestedFilter($"https://{ImageHost}/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnImageRequested;
            InitializeVideoPreview(core);
            core.WebMessageReceived += OnWebMessage;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationCompleted += OnNavigationCompleted;
            core.Navigate($"https://{AppHost}/index.html" + (_options.Demo ? "?demo&q=" + Uri.EscapeDataString(_options.Query ?? "") : ""));
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or System.Runtime.InteropServices.COMException or IOException)
        {
            MessageBox.Show(this,
                "EasySearch needs the Microsoft Edge WebView2 Runtime to draw its interface.\n\n" + e.Message,
                "EasySearch", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Quit();
        }
    }

    /// <summary>Serves file icons and thumbnails to the interface as ordinary image URLs.</summary>
    void OnImageRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        if (uri.Host != ImageHost) return;
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        string? path = query["p"];
        if (string.IsNullOrEmpty(path) || !int.TryParse(query["s"], out int size))
        {
            e.Response = _web.CoreWebView2.Environment.CreateWebResourceResponse(null, 400, "Bad Request", "");
            return;
        }

        var deferral = e.GetDeferral();
        _images.Request(path, size, thumbnail: uri.AbsolutePath == "/thumb", isDirectory: query["d"] == "1", png =>
        {
            try
            {
                BeginInvoke(() =>
                {
                    try
                    {
                        var environment = _web.CoreWebView2.Environment;
                        e.Response = png is null
                            ? environment.CreateWebResourceResponse(null, 404, "Not Found", "")
                            : environment.CreateWebResourceResponse(new MemoryStream(png), 200, "OK",
                                "Content-Type: image/png\r\nCache-Control: max-age=300");
                    }
                    finally
                    {
                        deferral.Complete();
                    }
                });
            }
            catch (InvalidOperationException)
            {
                // Window is closing.
            }
        });
    }

    async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!_options.Demo) return;
        // The sample-data interface never talks to the host, so size the window for it here.
        var ratio = await _web.CoreWebView2.ExecuteScriptAsync("devicePixelRatio");
        if (double.TryParse(ratio, System.Globalization.CultureInfo.InvariantCulture, out double scale)) SetUiScale(scale);
        if (_options.Capture is not null) CaptureAndQuit(_options.Capture);
    }

    async void CaptureAndQuit(string file)
    {
        await Task.Delay(3500);
        try
        {
            using (var stream = File.Create(file))
                await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);

            var viewport = await _web.CoreWebView2.ExecuteScriptAsync("[innerWidth, innerHeight, devicePixelRatio]");
            File.WriteAllText(file + ".txt",
                $"window {Bounds} client {ClientSize} dpi {DeviceDpi} viewport {viewport} foreground {Native.GetForegroundWindow() == Handle} hotkey {(_hotkeyHook is not null ? "hook" : _hotkeyRegistered ? "registered" : "none")}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
        }
        Quit();
    }
}
