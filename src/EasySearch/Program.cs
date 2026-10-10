namespace EasySearch;

/// <param name="Background">Start hidden in the tray (used when launching at login).</param>
/// <param name="Stay">Development: keep the window open when it loses focus.</param>
/// <param name="Query">Open with this text already in the search box.</param>
/// <param name="Capture">Development: save a PNG of the interface to this file, then exit.</param>
/// <param name="OpenSettings">Open on the settings page.</param>
/// <param name="Passive">Development: show without ever taking keyboard focus.</param>
/// <param name="Demo">Development: run the interface on its built-in sample data (for screenshots).</param>
public sealed record StartupOptions(bool Background, bool Stay, string? Query, string? Capture, bool OpenSettings, bool Demo, bool Passive)
{
    public static StartupOptions Parse(string[] args)
    {
        string? Value(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        return new StartupOptions(
            Background: args.Contains("--background"),
            Stay: args.Contains("--stay"),
            Query: Value("--query"),
            Capture: Value("--capture"),
            OpenSettings: args.Contains("--settings"),
            Demo: args.Contains("--demo"),
            Passive: args.Contains("--passive"));
    }
}

static class Program
{
    const string InstanceName = "EasySearch.Instance";
    const string ShowSignalName = "EasySearch.Show";

    [STAThread]
    static void Main(string[] args)
    {
        using var instance = new Mutex(initiallyOwned: true, InstanceName, out bool first);
        if (!first)
        {
            // Already running: ask that instance to show itself instead of starting another.
            if (EventWaitHandle.TryOpenExisting(ShowSignalName, out var signal))
            {
                signal.Set();
                signal.Dispose();
            }
            return;
        }

        ApplicationConfiguration.Initialize();
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        using var form = new MainForm(StartupOptions.Parse(args));

        var listener = new Thread(() =>
        {
            while (showSignal.WaitOne())
            {
                if (form.IsDisposed) return;
                try { form.BeginInvoke(form.ShowPalette); }
                catch (InvalidOperationException) { return; }
            }
        })
        { IsBackground = true, Name = "EasySearch instance signal" };
        listener.Start();

        Application.Run(form);
    }
}
