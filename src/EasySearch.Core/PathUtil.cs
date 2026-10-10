namespace EasySearch.Core;

public static class PathUtil
{
    const long UnixEpochTicks = 621355968000000000;

    /// <summary>
    /// Full path with backslashes and no trailing separator. Drive roots become "C:" so that
    /// every indexed path is simply its ancestors' names joined by '\'.
    /// </summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path.Trim().Trim('"')).Replace('/', '\\');
        while (full.Length > 2 && full[^1] == '\\') full = full[..^1];
        return full;
    }

    /// <summary>Inverse of <see cref="Normalize"/> for drive roots: "C:" becomes "C:\".</summary>
    public static string Display(string normalized) =>
        normalized.Length == 2 && normalized[1] == ':' ? normalized + "\\" : normalized;

    public static bool IsSameOrUnder(ReadOnlySpan<char> path, ReadOnlySpan<char> ancestor) =>
        path.Length >= ancestor.Length
        && path[..ancestor.Length].Equals(ancestor, StringComparison.OrdinalIgnoreCase)
        && (path.Length == ancestor.Length || path[ancestor.Length] == '\\');

    public static long ToUnixMs(long utcTicks) => (utcTicks - UnixEpochTicks) / TimeSpan.TicksPerMillisecond;

    public static long FromUnixMs(long unixMs) => unixMs * TimeSpan.TicksPerMillisecond + UnixEpochTicks;
}
