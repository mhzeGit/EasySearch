using System.Runtime.InteropServices;
using System.Text;

namespace EasySearch;

/// <summary>
/// Finds the folder shown by the File Explorer window that has focus, so the palette can search
/// it straight away. Only real File Explorer windows count: other apps, the desktop and the
/// taskbar are ignored.
/// </summary>
static class ExplorerFolder
{
    const string ExplorerClass = "CabinetWClass";

    static readonly Guid TopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    static readonly Guid ShellBrowserId = new("000214E2-0000-0000-C000-000000000046");

    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellBrowserWindow? browser);
    }

    /// <summary>Only the IOleWindow part of IShellBrowser is needed: where its window is.</summary>
    [ComImport, Guid("000214e2-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellBrowserWindow
    {
        [PreserveSig]
        int GetWindow(out IntPtr window);
    }

    /// <summary>The File Explorer window that has focus right now, or zero when focus is anywhere else.</summary>
    public static IntPtr ForegroundExplorer(IntPtr self)
    {
        var window = Native.GetForegroundWindow();
        if (window == IntPtr.Zero || window == self) return IntPtr.Zero;

        var name = new StringBuilder(64);
        return Native.GetClassNameW(window, name, name.Capacity) > 0 && name.ToString() == ExplorerClass ? window : IntPtr.Zero;
    }

    /// <summary>
    /// The folder of that window's current tab, or null for places that are not a folder on disk
    /// (This PC, Home, Quick access ...). Shell automation wants a single-threaded apartment, so
    /// it gets a thread of its own.
    /// </summary>
    public static Task<string?> FolderAsync(IntPtr window)
    {
        var done = new TaskCompletionSource<string?>();
        var thread = new Thread(() =>
        {
            try { done.SetResult(FolderOf(window)); }
            catch (Exception) { done.SetResult(null); }
        })
        { IsBackground = true, Name = "Explorer folder" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    /// <summary>Public for tests and diagnostics; call it on an STA thread.</summary>
    public static string? FolderOf(IntPtr window)
    {
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null) return null;

        dynamic shell = Activator.CreateInstance(type)!;
        string? fallback = null;
        try
        {
            dynamic windows = shell.Windows();
            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic item = windows.Item(i);
                    if (item is null) continue;
                    if (new IntPtr(Convert.ToInt64((object)item.HWND)) != window) continue;

                    string? path = item.Document.Folder.Self.Path;
                    if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) continue;

                    // Tabbed Explorer lists every tab under the same window; the open one is the visible one.
                    if (IsActiveTab((object)item)) return path;
                    fallback ??= path;
                }
                catch (Exception e) when (e is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    // A window that closed while we were looking.
                }
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject((object)shell);
        }
        return fallback;
    }

    static bool IsActiveTab(object item)
    {
        try
        {
            var service = TopLevelBrowser;
            var id = ShellBrowserId;
            if (item is not IShellServiceProvider provider || provider.QueryService(ref service, ref id, out var browser) != 0 || browser is null)
                return true;
            return browser.GetWindow(out var tab) != 0 || tab == IntPtr.Zero || Native.IsWindowVisible(tab);
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            return true;
        }
    }
}
