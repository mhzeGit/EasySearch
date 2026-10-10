using System.Text.Json;
using Microsoft.Win32;

namespace EasySearch;

/// <summary>User preferences, stored as JSON in %APPDATA%\EasySearch\settings.json.</summary>
public sealed class AppSettings
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const int CurrentExclusionsVersion = 1;
    const int CurrentSettingsVersion = 1;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Set EASYSEARCH_DATA to keep settings and the index in a folder of your choice (portable use, testing).</summary>
    static readonly string? Portable = Environment.GetEnvironmentVariable("EASYSEARCH_DATA") is { Length: > 0 } folder ? folder : null;

    public static string DataFolder { get; } =
        Portable ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasySearch");

    public static string CacheFolder { get; } = Portable is null
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasySearch")
        : Path.Combine(Portable, "cache");

    static string FilePath => Path.Combine(DataFolder, "settings.json");

    /// <summary>system, light or dark.</summary>
    public string Theme { get; set; } = "system";
    public string Accent { get; set; } = "#0a84ff";
    public string Hotkey { get; set; } = "Win+Space";
    public bool HideOnBlur { get; set; } = true;
    public bool LaunchAtLogin { get; set; } = true;
    public int SettingsVersion { get; set; }
    public bool ShowPreview { get; set; } = true;

    /// <summary>When opened from a File Explorer window, search that window's folder.</summary>
    public bool ScopeToExplorer { get; set; } = true;

    /// <summary>Folders and drives to index.</summary>
    public List<string> Roots { get; set; } = [];
    /// <summary>Folder names (anywhere) or full folder paths left out of the index.</summary>
    public List<string> Excluded { get; set; } = [];
    public int ExclusionsVersion { get; set; }

    public bool ShowHidden { get; set; }
    public bool ShowSystem { get; set; }
    public string DefaultSort { get; set; } = "relevance";

    public static AppSettings Load()
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings: start from the defaults.
        }

        if (settings is null)
        {
            settings = new AppSettings { Excluded = DefaultExclusions() };
        }
        // Turn on launch-at-login once for existing installs as well as new installs.
        // The version marker preserves a user's choice to turn it off on later launches.
        if (settings.SettingsVersion < CurrentSettingsVersion)
        {
            settings.LaunchAtLogin = true;
            settings.SettingsVersion = CurrentSettingsVersion;
            settings.Save();
        }
        if (settings.ExclusionsVersion < CurrentExclusionsVersion)
        {
            settings.Excluded ??= [];
            if (!settings.Excluded.Contains("AppData", StringComparer.OrdinalIgnoreCase)) settings.Excluded.Add("AppData");
            if (!settings.Excluded.Contains("Temp", StringComparer.OrdinalIgnoreCase)) settings.Excluded.Add("Temp");
            settings.ExclusionsVersion = CurrentExclusionsVersion;
            settings.Save();
        }
        if (settings.Roots.Count == 0) settings.Roots = DefaultRoots();
        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Every fixed drive.</summary>
    public static List<string> DefaultRoots() =>
        [.. DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.Name)];

    public static List<string> DefaultExclusions()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return
        [
            "$Recycle.Bin",
            "System Volume Information",
            "AppData",
            "Temp",
            "node_modules",
            ".git",
            Path.Combine(windows, "WinSxS"),
            Path.Combine(windows, "servicing"),
            Path.Combine(windows, "Installer"),
        ];
    }

    public static void ApplyLaunchAtLogin(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue("EasySearch", $"\"{Application.ExecutablePath}\" --background");
            else key.DeleteValue("EasySearch", throwOnMissingValue: false);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
        }
    }

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return true;
        }
    }
}
