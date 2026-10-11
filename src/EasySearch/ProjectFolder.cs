using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace EasySearch;

/// <summary>Best-effort current folder discovery from the active Windows application.</summary>
static partial class ProjectFolder
{
    public static Task<string?> FolderAsync(IntPtr window) => Task.Run(() => FolderOf(window));

    static string? FolderOf(IntPtr window)
    {
        // Unity and Unreal expose the open project path in their process arguments. Read this
        // inexpensive source first; traversing large editor accessibility trees can take seconds.
        try
        {
            Native.GetWindowThreadId(window, out uint processId);
            string? project = FindProjectArgument(ReadCommandLine(processId));
            if (project is not null) return project;
        }
        catch { }

        try
        {
            var root = AutomationElement.FromHandle(window);
            var walker = TreeWalker.ControlViewWalker;
            var current = root;
            // Address bars and folder pickers expose their current value as edit controls.
            for (int depth = 0; depth < 6 && current is not null; depth++, current = walker.GetFirstChild(current))
            {
                string? found = FindElementPath(current);
                if (found is not null) return found;
            }
            var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
            foreach (AutomationElement edit in root.FindAll(TreeScope.Descendants, condition))
            {
                string? found = FindElementPath(edit);
                if (found is not null) return found;
            }
        }
        catch { }

        try
        {
            var title = new StringBuilder(1024);
            Native.GetWindowTextW(window, title, title.Capacity);
            string? path = FindPath(title.ToString());
            if (path is not null) return path;

            return null;
        }
        catch { return null; }
    }

    static string? FindProjectArgument(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        // Unity and Unreal commonly pass their open project as an option argument.
        var project = Regex.Match(commandLine, "(?:-projectPath|-project|/project)\\s+(?:\\\"([^\\\"]+)\\\"|(\\S+))", RegexOptions.IgnoreCase);
        if (project.Success)
        {
            string value = project.Groups[1].Success ? project.Groups[1].Value : project.Groups[2].Value;
            string? folder = ExistingPath(value);
            if (folder is not null) return folder;
        }
        return FindPath(commandLine);
    }

    static string? ExistingPath(string value)
    {
        try
        {
            value = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
            if (!Path.IsPathRooted(value)) return null;
            if (File.Exists(value)) value = Path.GetDirectoryName(value)!;
            return Directory.Exists(value) ? Path.GetFullPath(value) : null;
        }
        catch { return null; }
    }

    static string? ReadCommandLine(uint processId)
    {
        if (processId == 0) return null;
        try
        {
            using var query = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
            foreach (ManagementObject item in query.Get()) return item["CommandLine"]?.ToString();
        }
        catch { }
        return null;
    }

    static string? FindElementPath(AutomationElement element)
    {
        try
        {
            string value = element.Current.Name;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
                value += " " + ((ValuePattern)pattern).Current.Value;
            return FindPath(value);
        }
        catch { return null; }
    }

    static string? FindPath(string text)
    {
        foreach (Match match in PathRegex().Matches(text))
        {
            string candidate = match.Value.Trim().Trim('"').TrimEnd('.', ',', ';', ')', ']');
            try
            {
                if (File.Exists(candidate)) candidate = Path.GetDirectoryName(candidate)!;
                if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch { }
        }
        return null;
    }

    [GeneratedRegex("[A-Za-z]:\\\\[^\\\"\\r\\n<>|?*]+")]
    private static partial Regex PathRegex();

}
