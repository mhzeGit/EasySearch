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
    int _statusDirty;
    double _uiScale;

    public MainForm(StartupOptions options)
    {
        _options = options;
        _startHidden = options.Background;
        _settings = AppSettings.Load();

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
        if (value && !Visible)
        {
            // About to appear: settle the position, then photograph what will be behind the window.
            if (!IsHandleCreated) CreateHandle();
            PlaceOnScreen();
            _backdrop.Capture(Screen.FromRectangle(Bounds).Bounds);
            Invalidate(false);
        }
        base.SetVisibleCore(value);
        Initialize();
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
        _tray.Visible = false;
        _tray.Dispose();
        _index.Dispose();
        _images.Dispose();
        _backdrop.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        _backdrop.Paint(e.Graphics, Bounds, DarkTheme, SurfaceColor);

    void ApplyChrome()
    {
        bool dark = DarkTheme;
        int value = dark ? 1 : 0;
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        value = Native.DWMWCP_ROUND;
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
        value = dark ? 0x00484848 : 0x00BDBDBD; // COLORREF
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_BORDER_COLOR, ref value, sizeof(int));

        BackColor = SurfaceColor;
        Invalidate(false); // the backdrop is toned differently for light and dark
    }

    /// <summary>Centers the palette on the monitor the pointer is on, a little above the middle.</summary>
    void PlaceOnScreen()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = area.Location; // move first so the window adopts that monitor's DPI
        double scale = _uiScale > 0 ? _uiScale : DeviceDpi / 96.0;
        var size = new Size(
            Math.Min((int)(BaseSize.Width * scale), area.Width - 24),
            Math.Min((int)(BaseSize.Height * scale), area.Height - 24));
        Bounds = new Rectangle(
            area.Left + (area.Width - size.Width) / 2,
            area.Top + (int)((area.Height - size.Height) * 0.38),
            size.Width, size.Height);
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

    public void ShowPalette()
    {
        Show(); // positions the window and captures the backdrop when it was hidden
        Activate();
        Native.SetForegroundWindow(Handle);
        _web.Focus();
        Post(new { type = "shown" });
    }

    void Toggle()
    {
        if (Visible && Native.GetForegroundWindow() == Handle) Hide();
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
        if (!TryParseHotkey(_settings.Hotkey, out uint modifiers, out uint key)) return false;
        _hotkeyRegistered = Native.RegisterHotKey(Handle, HotkeyId, modifiers | Native.MOD_NOREPEAT, key);
        return _hotkeyRegistered;
    }

    void UnregisterHotkey()
    {
        if (_hotkeyRegistered) Native.UnregisterHotKey(Handle, HotkeyId);
        _hotkeyRegistered = false;
    }

    /// <summary>Parses "Ctrl+Alt+Space" style text. At least one modifier is required.</summary>
    static bool TryParseHotkey(string text, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= Native.MOD_CONTROL; break;
                case "alt": modifiers |= Native.MOD_ALT; break;
                case "shift": modifiers |= Native.MOD_SHIFT; break;
                case "win" or "meta": modifiers |= Native.MOD_WIN; break;
                default:
                    if (part.Length == 1 && char.IsAsciiLetterOrDigit(part[0])) key = char.ToUpperInvariant(part[0]);
                    else if (Enum.TryParse<Keys>(part, ignoreCase: true, out var parsed) && !char.IsAsciiDigit(part[0])) key = (uint)parsed;
                    else return false;
                    break;
            }
        }
        return key != 0 && modifiers != 0;
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
            settings.IsNonClientRegionSupportEnabled = true; // lets CSS "app-region: drag" move the window
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
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        string? path = query["p"];
        if (string.IsNullOrEmpty(path) || !int.TryParse(query["s"], out int size))
        {
            e.Response = _web.CoreWebView2.Environment.CreateWebResourceResponse(null, 400, "Bad Request", "");
            return;
        }

        var deferral = e.GetDeferral();
        _images.Request(path, size, thumbnail: uri.AbsolutePath == "/thumb", png =>
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
                $"window {Bounds} client {ClientSize} dpi {DeviceDpi} viewport {viewport} foreground {Native.GetForegroundWindow() == Handle}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
        }
        Quit();
    }
}
