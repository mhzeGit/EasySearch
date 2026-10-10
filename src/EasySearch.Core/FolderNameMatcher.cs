namespace EasySearch.Core;

static class FolderNameMatcher
{
    public static string[] Resolve(IndexSnapshot snapshot, string requested)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < snapshot.Count; i++)
            if ((snapshot.Flags[i] & (EntryFlags.Directory | EntryFlags.Deleted)) == EntryFlags.Directory
                && snapshot.Parent[i] >= 0)
                names.Add(snapshot.Name(i).ToString());

        if (names.TryGetValue(requested, out var exact)) return [exact];
        var partial = names.Where(n => n.Contains(requested, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (partial.Length > 0) return partial;

        var closest = new List<string>();
        int best = int.MaxValue;
        foreach (var name in names)
        {
            int distance = Distance(requested, name);
            if (distance > best) continue;
            if (distance < best) { closest.Clear(); best = distance; }
            closest.Add(name);
        }
        return [.. closest];
    }

    static int Distance(string requested, string candidate)
    {
        var costs = Enumerable.Range(0, candidate.Length + 1).ToArray();
        for (int row = 1; row <= requested.Length; row++)
        {
            int diagonal = costs[0];
            costs[0] = row;
            for (int column = 1; column <= candidate.Length; column++)
            {
                int previous = costs[column];
                int replacement = char.ToUpperInvariant(requested[row - 1]) == char.ToUpperInvariant(candidate[column - 1]) ? 0 : 1;
                costs[column] = Math.Min(diagonal + replacement, Math.Min(costs[column - 1], previous) + 1);
                diagonal = previous;
            }
        }
        return costs[^1];
    }
}
