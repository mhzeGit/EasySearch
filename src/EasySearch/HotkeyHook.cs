using System.Runtime.InteropServices;

namespace EasySearch;

/// <summary>
/// Claims a key combination that Windows will not hand out through RegisterHotKey — most
/// notably Win+Space, which the system reserves for switching keyboard layouts.
///
/// It is a low-level keyboard hook: Windows calls <see cref="OnKey"/> for every key press, and
/// the combination is swallowed there before the system sees it. The callback does nothing but
/// compare a few numbers, so it adds no measurable typing latency. It is only installed when the
/// normal hotkey registration is refused.
///
/// The hook lives on a thread of its own. Windows silently drops a hook whose thread is slow to
/// answer, so it must not share the UI thread, which is sometimes busy for a moment.
/// </summary>
sealed class HotkeyHook : IDisposable
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105, WM_QUIT = 0x0012;
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    const byte VK_UNASSIGNED = 0xFF;
    const uint KEYEVENTF_KEYUP = 2;

    readonly HookProc _callback; // kept in a field so the delegate outlives the native hook
    readonly Action _pressed;
    readonly Action? _contextPressed;
    readonly uint _key;
    Thread? _thread;
    uint _threadId;
    IntPtr _hook;
    bool _held;
    bool _discardHeld;

    HotkeyHook(uint key, Action pressed, Action? contextPressed = null)
    {
        _key = key;
        _pressed = pressed;
        _contextPressed = contextPressed;
        _callback = OnKey;
    }

    /// <summary>
    /// Installs the hook. <paramref name="pressed"/> is called on the hook's own thread and must
    /// return at once (hand the work to the UI thread). Returns null if Windows refuses the hook.
    /// </summary>
    public static HotkeyHook? TryInstall(uint key, Action pressed, Action? contextPressed = null)
    {
        var hook = new HotkeyHook(key, pressed, contextPressed);
        using var installed = new ManualResetEventSlim();
        hook._thread = new Thread(() =>
        {
            hook._threadId = GetCurrentThreadId();
            hook._hook = SetWindowsHookExW(WH_KEYBOARD_LL, hook._callback, GetModuleHandleW(null), 0);
            installed.Set();
            if (hook._hook == IntPtr.Zero) return;

            // Windows delivers the hook calls to this thread while it waits for messages.
            while (GetMessageW(out _, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWindowsHookEx(hook._hook);
        })
        { IsBackground = true, Name = "EasySearch hotkey" };
        hook._thread.Start();
        installed.Wait();
        return hook._hook == IntPtr.Zero ? null : hook;
    }

    IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && (uint)Marshal.ReadInt32(data) == _key) // KBDLLHOOKSTRUCT.vkCode
        {
            int kind = (int)message;
            if (kind is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                if (_held) return _discardHeld ? 1 : CallNextHookEx(_hook, code, message, data);
                uint current = CurrentModifiers();
                bool winSpace = _key == 0x20 && (current & Native.MOD_WIN) != 0;
                if (winSpace && current == Native.MOD_WIN)
                {
                    _held = true;
                    _discardHeld = true;
                    // Windows never saw the second key, so releasing Win alone would open Start.
                    Native.keybd_event(VK_UNASSIGNED, 0, 0, UIntPtr.Zero);
                    Native.keybd_event(VK_UNASSIGNED, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    _pressed();
                    return 1;
                }
                if (winSpace && _contextPressed is not null)
                {
                    _held = _discardHeld = true;
                    Native.keybd_event(VK_UNASSIGNED, 0, 0, UIntPtr.Zero);
                    Native.keybd_event(VK_UNASSIGNED, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    if (current == (Native.MOD_WIN | Native.MOD_ALT)) _contextPressed();
                    return 1;
                }
            }
            else if (kind is WM_KEYUP or WM_SYSKEYUP && _held)
            {
                _held = false;
                if (_discardHeld) { _discardHeld = false; return 1; }
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    static uint CurrentModifiers()
    {
        static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
        uint modifiers = 0;
        if (Down(VK_CONTROL)) modifiers |= Native.MOD_CONTROL;
        if (Down(VK_MENU)) modifiers |= Native.MOD_ALT;
        if (Down(VK_SHIFT)) modifiers |= Native.MOD_SHIFT;
        if (Down(VK_LWIN) || Down(VK_RWIN)) modifiers |= Native.MOD_WIN;
        return modifiers;
    }

    public void Dispose()
    {
        if (_thread is null) return;
        PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero); // ends the loop, which removes the hook
        _thread.Join(1000);
        _thread = null;
    }

    delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam, LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookExW(int hook, HookProc callback, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    static extern int GetMessageW(out MSG message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    static extern bool PostThreadMessageW(uint thread, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandleW(string? name);
}
