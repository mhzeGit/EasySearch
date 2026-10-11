namespace EasySearch.Core;

/// <summary>Suggests immediate child directories for a typed directory path.</summary>
public static class DirectoryPrediction
{
    public static string[] FindAll(string path, string prefix = "", CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return [];
        try
        {
            var matches = new List<string>();
            using var directories = Directory.EnumerateDirectories(path).GetEnumerator();
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                if (!directories.MoveNext()) break;
                var directory = directories.Current;
                var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
                if (!string.IsNullOrEmpty(name) && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) matches.Add(name);
            }
            return matches
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(name => name, StringComparer.Ordinal)
                .Take(100)
                .Select(name => name!)
                .ToArray();
        }
        catch (UnauthorizedAccessException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        catch (IOException) { return []; }
    }
}
