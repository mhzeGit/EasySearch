using System.Runtime.InteropServices;
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
            return FindPath(title.ToString());
        }
        catch { return null; }
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
