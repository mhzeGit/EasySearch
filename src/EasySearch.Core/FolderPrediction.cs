namespace EasySearch.Core;

public static class FolderPrediction
{
    public static string? Find(FileIndex index, string prefix, CancellationToken cancel = default) => FindAll(index, prefix, cancel).FirstOrDefault();

    public static string[] FindAll(FileIndex index, string prefix, CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return [];
        var snapshot = index.Snapshot();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < snapshot.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            if ((snapshot.Flags[i] & (EntryFlags.Directory | EntryFlags.Deleted)) == EntryFlags.Directory)
                names.Add(snapshot.Name(i).ToString());
        }
        return names.Select(name => (Name: name, Rank: name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? 0
                : name.Contains(prefix, StringComparison.OrdinalIgnoreCase) ? 1 : 2,
                Distance: Distance(prefix, name)))
            .OrderBy(item => item.Rank).ThenBy(item => item.Distance)
            .ThenBy(item => item.Name.Length).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(20).Select(item => item.Name).ToArray();
    }

    static int Distance(string prefix, string name)
    {
        var costs = Enumerable.Range(0, name.Length + 1).ToArray();
        for (int row = 1; row <= prefix.Length; row++)
        {
            int diagonal = costs[0];
            costs[0] = row;
            for (int col = 1; col <= name.Length; col++)
            {
                int previous = costs[col];
                costs[col] = Math.Min(diagonal + (char.ToUpperInvariant(prefix[row - 1]) == char.ToUpperInvariant(name[col - 1]) ? 0 : 1),
                    Math.Min(costs[col - 1], previous) + 1);
                diagonal = previous;
            }
        }
        // Compare against prefixes too, so a typo in a partially typed name can complete.
        return costs.Skip(Math.Min(prefix.Length, name.Length)).Min();
    }
}
