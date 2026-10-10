namespace EasySearch.Core;

/// <summary>
/// What gets indexed: a set of locations, minus excluded folders. An exclusion is either a bare
/// folder name ("node_modules", skipped wherever it appears) or a full folder path.
/// </summary>
public sealed class IndexScope
{
    readonly HashSet<string> _excludedNames = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _excludedPaths = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _rootSet = new(StringComparer.OrdinalIgnoreCase);

    public IndexScope(IEnumerable<string> roots, IEnumerable<string> excluded)
    {
        var list = new List<string>();
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            string normalized;
            try { normalized = PathUtil.Normalize(root); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { continue; }
            if (_rootSet.Add(normalized)) list.Add(normalized);
        }
        Roots = list;

        foreach (var entry in excluded)
        {
            var value = entry.Trim().Trim('"');
            if (value.Length == 0) continue;
            if (value.Contains('\\') || value.Contains('/') || value.Contains(':'))
            {
                try { _excludedPaths.Add(PathUtil.Normalize(value)); }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { }
            }
            else
            {
                _excludedNames.Add(value);
            }
        }

        Signature = string.Join("|", Roots.Order(StringComparer.OrdinalIgnoreCase)) + "//"
            + string.Join("|", _excludedNames.Order(StringComparer.OrdinalIgnoreCase)) + "//"
            + string.Join("|", _excludedPaths.Order(StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> Roots { get; }

    public string Signature { get; }

    public bool IsExcludedName(ReadOnlySpan<char> folderName) =>
        _excludedNames.Count > 0 && _excludedNames.GetAlternateLookup<ReadOnlySpan<char>>().Contains(folderName);

    /// <summary>
    /// True for explicitly excluded folders, and for folders that are index locations of their
    /// own (those are scanned as separate trees, so a parent location must not descend into them).
    /// </summary>
    public bool IsExcludedPath(string normalizedFolderPath) =>
        _excludedPaths.Contains(normalizedFolderPath) || _rootSet.Contains(normalizedFolderPath);
}
