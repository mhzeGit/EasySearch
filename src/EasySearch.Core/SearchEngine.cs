using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace EasySearch.Core;

public sealed class SearchHit
{
    public required string Name { get; init; }
    /// <summary>Containing folder; empty for an index location itself.</summary>
    public required string Dir { get; init; }
    public bool IsDir { get; init; }
    public long Size { get; init; }
    /// <summary>Unix milliseconds.</summary>
    public long Modified { get; init; }
    public bool Hidden { get; init; }

    public string Path => Dir.Length == 0 ? Name : System.IO.Path.Combine(Dir, Name);
}

public sealed class SearchResult
{
    public int Total { get; init; }
    public double ElapsedMs { get; init; }
    public List<SearchHit> Items { get; init; } = [];
    public string? Error { get; init; }
}

/// <summary>
/// Runs a query as a parallel scan over the whole index, keeping only the best
/// <see cref="SearchRequest.Limit"/> hits per thread so huge result sets stay cheap.
/// </summary>
public static class SearchEngine
{
    public const int MaxLimit = 10_000;

    /// <summary>Threads used per search; -1 lets the runtime decide.</summary>
    public static int MaxParallelism { get; set; } = -1;

    enum SortKey { Relevance, Name, Path, Size, Modified }

    readonly record struct Hit(int Index, int Score);

    public static SearchResult Run(FileIndex index, SearchRequest request, CancellationToken cancel = default, Action<SearchResult>? onProgress = null)
    {
        var timer = Stopwatch.StartNew();
        List<CompiledQuery> branches;
        try
        {
            branches = QueryParser.Parse(request, DateTime.Now);
        }
        catch (FormatException e)
        {
            return new SearchResult { Error = e.Message };
        }
        var query = branches[0]; // the branches share everything but their terms and filters

        var snapshot = index.Snapshot();
        if (snapshot.Count == 0) return new SearchResult { ElapsedMs = timer.Elapsed.TotalMilliseconds };

        // Resolve a misspelled inside: value once, rather than repeating fuzzy work for every hit.
        var insideNames = branches
            .SelectMany(b => b.Inside)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(name => name, name => FolderNameMatcher.Resolve(snapshot, name), StringComparer.OrdinalIgnoreCase);

        var scopes = new Dictionary<string, bool[]>(StringComparer.OrdinalIgnoreCase);
        var scopeMasks = new bool[]?[branches.Count];
        for (int b = 0; b < branches.Count; b++)
        {
            if (branches[b].Scope is not { } folder) continue;
            if (!scopes.TryGetValue(folder, out var mask)) scopes[folder] = mask = index.BuildScopeMask(folder, snapshot);
            scopeMasks[b] = mask;
        }

        var sort = request.Sort.ToLowerInvariant() switch
        {
            "name" => SortKey.Name,
            "path" => SortKey.Path,
            "size" => SortKey.Size,
            "modified" or "date" => SortKey.Modified,
            _ => SortKey.Relevance,
        };
        bool descending = request.Desc;
        if (sort == SortKey.Relevance && branches.All(b => b.Primary < 0))
        {
            // Nothing to rank by (filters only): newest first is the most useful order.
            sort = SortKey.Modified;
            descending = true;
        }

        int limit = Math.Clamp(request.Limit, 1, MaxLimit);
        var ranking = new Ranking(snapshot, sort, descending);
        var worstFirst = Comparer<Hit>.Create((a, b) => ranking.Compare(b, a));
        var matcher = new Matcher(
            [.. branches.Select((b, i) => new BranchMatcher(snapshot, b, scopeMasks[i], sort == SortKey.Relevance,
                [.. b.Inside.Select(inside => insideNames[inside])]))]);

        // With OR branches there is no single piece of text every match must contain.
        string? driver = branches.Count == 1 ? FindDriver(query) : null;

        var best = new List<Hit>();
        long total = 0;
        long lastProgress = 0;
        int rangeSize = Math.Max(8192, snapshot.Count / (Environment.ProcessorCount * 4));

        Parallel.ForEach(
            Partitioner.Create(0, snapshot.Count, rangeSize),
            new ParallelOptions { CancellationToken = cancel, MaxDegreeOfParallelism = MaxParallelism },
            () => new Local(limit, ranking, worstFirst),
            (range, loop, local) =>
            {
                if (cancel.IsCancellationRequested) { loop.Stop(); Flush(local); return local; }
                if (driver is not null)
                {
                    ScanNames(snapshot, range.Item1, range.Item2, driver, query.Comparison, matcher, local, cancel,
                        onProgress is null ? null : () => Flush(local));
                }
                else for (int i = range.Item1; i < range.Item2; i++)
                {
                    if ((i & 0x3FFF) == 0 && cancel.IsCancellationRequested) { loop.Stop(); break; }
                    if (matcher.IsMatch(i, local, out int score)) local.Add(new Hit(i, score));
                    if ((i & 0x3FFF) == 0) Flush(local);
                }
                Flush(local);
                return local;
            },
            Flush);

        cancel.ThrowIfCancellationRequested();
        return CreateResult([.. best], total);

        void Flush(Local local)
        {
            Hit[]? partialHits = null;
            long partialTotal = 0;
            lock (best)
            {
                total += local.Total;
                local.Total = 0;
                while (local.Heap.TryDequeue(out var hit, out _)) best.Add(hit);
                if (best.Count > limit)
                {
                    best.Sort(ranking);
                    best.RemoveRange(limit, best.Count - limit);
                }

                long now = Stopwatch.GetTimestamp();
                if (onProgress is not null && (lastProgress == 0 || Stopwatch.GetElapsedTime(lastProgress, now).TotalMilliseconds >= 100))
                {
                    lastProgress = now;
                    partialHits = [.. best];
                    partialTotal = total;
                }
            }
            if (partialHits is not null) onProgress!(CreateResult(partialHits, partialTotal));
        }

        SearchResult CreateResult(Hit[] hits, long matchCount)
        {
            Array.Sort(hits, ranking);
            if (hits.Length > limit) Array.Resize(ref hits, limit);

            var items = new List<SearchHit>(hits.Length);
            foreach (var hit in hits)
            {
                int i = hit.Index;
                bool isRoot = snapshot.Parent[i] < 0;
                items.Add(new SearchHit
                {
                    Name = isRoot ? snapshot.GetPath(i) : snapshot.Name(i).ToString(),
                    Dir = snapshot.GetDirectory(i),
                    IsDir = snapshot.IsDirectory(i),
                    Size = snapshot.Size[i],
                    Modified = PathUtil.ToUnixMs(snapshot.Modified[i]),
                    Hidden = (snapshot.Flags[i] & (EntryFlags.Hidden | EntryFlags.HiddenTree)) != 0,
                });
            }

            return new SearchResult
            {
                Total = (int)Math.Min(matchCount, int.MaxValue),
                ElapsedMs = timer.Elapsed.TotalMilliseconds,
                Items = items,
            };
        }
    }

    static string? ResolveInsideName(IndexSnapshot snapshot, string requested)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < snapshot.Count; i++)
        {
            if ((snapshot.Flags[i] & (EntryFlags.Directory | EntryFlags.Deleted)) != EntryFlags.Directory
                || snapshot.Parent[i] < 0) continue;
            names.Add(snapshot.Name(i).ToString());
        }

        if (names.Contains(requested)) return names.First(name => name.Equals(requested, StringComparison.OrdinalIgnoreCase));

        string? closest = null;
        int bestDistance = int.MaxValue;
        foreach (var name in names)
        {
            int distance = EditDistance(requested, name);
            if (distance < bestDistance || (distance == bestDistance &&
                string.Compare(name, closest, StringComparison.OrdinalIgnoreCase) < 0))
            {
                closest = name;
                bestDistance = distance;
            }
        }
        return closest;
    }

    static int EditDistance(string left, string right)
    {
        // Folder names are short; two rows keep the cost bounded to O(lengths) memory.
        if (left.Length > right.Length) (left, right) = (right, left);
        var previous = new int[left.Length + 1];
        var current = new int[left.Length + 1];
        for (int i = 0; i <= left.Length; i++) previous[i] = i;
        for (int j = 1; j <= right.Length; j++)
        {
            current[0] = j;
            for (int i = 1; i <= left.Length; i++)
            {
                int substitution = char.ToUpperInvariant(left[i - 1]) == char.ToUpperInvariant(right[j - 1]) ? 0 : 1;
                current[i] = Math.Min(Math.Min(current[i - 1] + 1, previous[i] + 1), previous[i - 1] + substitution);
            }
            (previous, current) = (current, previous);
        }
        return previous[left.Length];
    }

    /// <summary>
    /// The longest piece of literal text every matching name must contain, if the query has one.
    /// It lets <see cref="ScanNames"/> skip straight to candidate entries.
    /// </summary>
    static string? FindDriver(CompiledQuery query)
    {
        string? driver = null;
        foreach (var term in query.Terms)
        {
            if (term.Negate || term.OnPath || term.Alternatives.Length != 1) continue;
            var alternative = term.Alternatives[0];
            if (alternative.Kind == MatchKind.Regex) continue;

            foreach (var literal in alternative.Kind == MatchKind.Wildcard ? alternative.Text.Split('*', '?') : [alternative.Text])
                if (literal.Length > (driver?.Length ?? 0)) driver = literal;
        }
        // A single character matches nearly everything; jumping between matches then costs more than it saves.
        return driver is { Length: > 1 } ? driver : null;
    }

    /// <summary>
    /// All names sit back to back in one buffer, so instead of testing entries one by one this
    /// runs a single vectorized search for <paramref name="driver"/> across the buffer and only
    /// evaluates the entries it lands in.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void ScanNames(IndexSnapshot snapshot, int from, int to, string driver, StringComparison comparison, Matcher matcher, Local local, CancellationToken cancel, Action? progress)
    {
        var names = snapshot.Names;
        var starts = snapshot.NameStart;
        int end = starts[to - 1] + snapshot.NameLength[to - 1];
        int searchFrom = starts[from];
        int lastEvaluatedEntry = from - 1;

        int matchesSinceProgress = 0;
        while (searchFrom < end)
        {
            if (cancel.IsCancellationRequested) return;
            int found = names.AsSpan(searchFrom, end - searchFrom).IndexOf(driver, comparison);
            if (found < 0) return;
            found += searchFrom;

            // Last entry whose name starts at or before the match.
            int e = Array.BinarySearch(starts, from, to - from, found);
            if (e < 0) e = ~e - 1;

            int nameEnd = starts[e] + snapshot.NameLength[e];
            // The name buffer packs entries back to back. A substring found across two adjacent
            // names is not a real hit; skip past that whole name instead of checking every entry
            // one by one (common words can otherwise make this path quadratic).
            if (e > lastEvaluatedEntry && found + driver.Length <= nameEnd)
            {
                lastEvaluatedEntry = e;
                if (matcher.IsMatch(e, local, out int score))
                {
                    local.Add(new Hit(e, score));
                    if (++matchesSinceProgress >= 128)
                    {
                        matchesSinceProgress = 0;
                        progress?.Invoke();
                    }
                }
            }
            // Search forward through the packed buffer once. Re-scanning the entire remaining
            // suffix from the next entry for every occurrence makes common queries quadratic.
            searchFrom = found + 1;
        }
    }

    /// <summary>Per-thread state: a bounded heap whose root is the worst hit kept so far.</summary>
    sealed class Local(int limit, Ranking ranking, IComparer<Hit> worstFirst)
    {
        public readonly PriorityQueue<Hit, Hit> Heap = new(worstFirst);
        public long Total;
        char[] _path = new char[512];
        Dictionary<Alternative, Regex>? _regexes;

        /// <summary>
        /// A Regex only caches one runner, so sharing an instance across threads makes every
        /// other thread allocate a fresh one per call. Each thread gets its own copy instead.
        /// </summary>
        public Regex RegexFor(Alternative alternative)
        {
            _regexes ??= [];
            if (!_regexes.TryGetValue(alternative, out var regex))
            {
                var shared = alternative.Regex!;
                _regexes[alternative] = regex = new Regex(shared.ToString(), shared.Options, shared.MatchTimeout);
            }
            return regex;
        }

        public void Add(Hit hit)
        {
            Total++;
            if (Heap.Count < limit) Heap.Enqueue(hit, hit);
            else if (ranking.Compare(hit, Heap.Peek()) < 0) Heap.EnqueueDequeue(hit, hit);
        }

        public ReadOnlySpan<char> PathOf(IndexSnapshot snapshot, int i)
        {
            int length = snapshot.PathLength(i);
            if (length > _path.Length) _path = new char[length * 2];
            snapshot.WritePath(i, _path);
            return _path.AsSpan(0, length);
        }
    }

    /// <summary>Matches when any OR branch does; the best branch's relevance wins.</summary>
    sealed class Matcher(BranchMatcher[] branches)
    {
        public bool IsMatch(int i, Local local, out int relevance)
        {
            if (branches.Length == 1) return branches[0].IsMatch(i, local, out relevance);

            relevance = 0;
            bool matched = false;
            foreach (var branch in branches)
            {
                if (!branch.IsMatch(i, local, out int branchRelevance)) continue;
                if (!matched || branchRelevance > relevance) relevance = branchRelevance;
                matched = true;
            }
            return matched;
        }
    }

    sealed class BranchMatcher(IndexSnapshot snapshot, CompiledQuery query, bool[]? scope, bool score, string[][] resolvedInside)
    {
        readonly Term[] _terms = [.. query.Terms];
        readonly HashSet<string>[] _required = [.. query.RequiredExtensions];
        readonly StringComparison _comparison = query.Comparison;
        readonly string[][] _inside = resolvedInside;
        readonly long _now = DateTime.UtcNow.Ticks;
        readonly EntryFlags _skip = EntryFlags.Deleted
            | (query.ShowHidden ? 0 : EntryFlags.Hidden | EntryFlags.HiddenTree)
            | (query.ShowSystem ? 0 : EntryFlags.System);

        // Compiled fully on first use, so the first search after start is as fast as the rest.
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public bool IsMatch(int i, Local local, out int relevance)
        {
            relevance = 0;
            var flags = snapshot.Flags[i];
            if ((flags & _skip) != 0) return false;

            bool isDirectory = (flags & EntryFlags.Directory) != 0;
            if (isDirectory ? query.FilesOnly : query.FoldersOnly) return false;
            if (scope is not null && !scope[i]) return false;

            if (query.HasSize)
            {
                long size = snapshot.Size[i];
                if (size < query.SizeMin || size > query.SizeMax) return false;
            }
            if (query.HasDate)
            {
                long modified = snapshot.Modified[i];
                if (modified < query.ModifiedMin || modified >= query.ModifiedMax) return false;
            }

            var name = snapshot.Name(i);
            foreach (var inside in _inside)
                if (!inside.Any(folder => HasParentFolderNamed(snapshot, i, folder))) return false;
            if (_required.Length > 0 || query.ForbiddenExtensions is not null)
            {
                if (isDirectory)
                {
                    if (_required.Length > 0) return false;
                }
                else
                {
                    var extension = FileKinds.ExtensionOf(name);
                    foreach (var set in _required)
                        if (!set.GetAlternateLookup<ReadOnlySpan<char>>().Contains(extension)) return false;
                    if (query.ForbiddenExtensions?.GetAlternateLookup<ReadOnlySpan<char>>().Contains(extension) == true)
                        return false;
                }
            }

            ReadOnlySpan<char> path = default;
            int primaryPosition = -1, primaryLength = 0;
            for (int t = 0; t < _terms.Length; t++)
            {
                var term = _terms[t];
                var text = name;
                if (term.OnPath)
                {
                    if (path.IsEmpty) path = local.PathOf(snapshot, i);
                    text = path;
                }

                bool matched = false;
                foreach (var alternative in term.Alternatives)
                {
                    switch (alternative.Kind)
                    {
                        case MatchKind.Substring:
                            int position = text.IndexOf(alternative.Text, _comparison);
                            matched = position >= 0;
                            if (matched && t == query.Primary) { primaryPosition = position; primaryLength = alternative.Text.Length; }
                            break;
                        case MatchKind.ExactName:
                            matched = !term.OnPath && name.Equals(alternative.Text, _comparison);
                            if (matched && t == query.Primary) { primaryPosition = 0; primaryLength = alternative.Text.Length; }
                            break;
                        case MatchKind.Wildcard:
                            matched = FileSystemName.MatchesSimpleExpression(alternative.Text, text, query.IgnoreCase);
                            break;
                        default:
                            if (alternative.TimedOut) break;
                            try { matched = local.RegexFor(alternative).IsMatch(text); }
                            catch (RegexMatchTimeoutException) { alternative.TimedOut = true; }
                            break;
                    }
                    if (matched) break;
                }
                if (matched == term.Negate) return false;
            }

            if (score) relevance = Score(i, name, flags, primaryPosition, primaryLength);
            return true;
        }

        bool HasParentFolderNamed(IndexSnapshot snapshot, int index, string folderName)
        {
            int parent = snapshot.Parent[index];
            while (parent >= 0 && snapshot.Parent[parent] >= 0)
            {
                if (snapshot.Name(parent).Equals(folderName, _comparison)) return true;
                parent = snapshot.Parent[parent];
            }
            return false;
        }

        /// <summary>Higher is better: exact and prefix matches first, then word starts, short names, shallow and recent items.</summary>
        int Score(int i, ReadOnlySpan<char> name, EntryFlags flags, int position, int length)
        {
            int value = 0;
            if (position == 0)
            {
                value += 400;
                int stem = name.LastIndexOf('.');
                if (length == name.Length || length == stem) value += 600;
            }
            else if (position > 0)
            {
                char before = name[position - 1], at = name[position];
                if (!char.IsLetterOrDigit(before) || (char.IsLower(before) && char.IsUpper(at))) value += 200;
            }

            value -= Math.Min(name.Length, 120);
            if ((flags & EntryFlags.Directory) != 0) value += 40;
            if ((flags & (EntryFlags.Hidden | EntryFlags.HiddenTree | EntryFlags.System)) != 0) value -= 150;
            if (name.Length > 0 && name[0] is '.' or '_' or '$' or '~') value -= 40;

            int depth = 0;
            for (int p = snapshot.Parent[i]; p >= 0; p = snapshot.Parent[p]) depth++;
            value -= Math.Min(depth, 24) * 6;

            long age = _now - snapshot.Modified[i];
            value += age < TimeSpan.TicksPerDay ? 80
                : age < 7 * TimeSpan.TicksPerDay ? 60
                : age < 30 * TimeSpan.TicksPerDay ? 40
                : age < 365 * TimeSpan.TicksPerDay ? 20 : 0;
            return value;
        }
    }

    /// <summary>Orders hits the way they are shown: <c>Compare(a, b) &lt; 0</c> means a comes first.</summary>
    sealed class Ranking(IndexSnapshot snapshot, SortKey key, bool descending) : IComparer<Hit>
    {
        [ThreadStatic] static char[]? t_left;
        [ThreadStatic] static char[]? t_right;

        public int Compare(Hit a, Hit b)
        {
            int order = key switch
            {
                SortKey.Relevance => Relevance(a, b),
                SortKey.Name => Direction(CompareNames(a.Index, b.Index)),
                SortKey.Path => Direction(ComparePaths(a.Index, b.Index)),
                SortKey.Size => Direction(snapshot.Size[a.Index].CompareTo(snapshot.Size[b.Index])),
                _ => Direction(snapshot.Modified[a.Index].CompareTo(snapshot.Modified[b.Index])),
            };
            if (order == 0 && key != SortKey.Name) order = CompareNames(a.Index, b.Index);
            return order != 0 ? order : a.Index.CompareTo(b.Index);
        }

        int Direction(int order) => descending ? -order : order;

        int Relevance(Hit a, Hit b)
        {
            int order = b.Score.CompareTo(a.Score);
            return order != 0 ? order : snapshot.NameLength[a.Index].CompareTo(snapshot.NameLength[b.Index]);
        }

        int CompareNames(int a, int b) =>
            snapshot.Name(a).CompareTo(snapshot.Name(b), StringComparison.OrdinalIgnoreCase);

        int ComparePaths(int a, int b)
        {
            var left = PathOf(a, ref t_left);
            var right = PathOf(b, ref t_right);
            return left.CompareTo(right, StringComparison.OrdinalIgnoreCase);
        }

        ReadOnlySpan<char> PathOf(int i, ref char[]? buffer)
        {
            int length = snapshot.PathLength(i);
            if (buffer is null || buffer.Length < length) buffer = new char[Math.Max(1024, length * 2)];
            snapshot.WritePath(i, buffer);
            return buffer.AsSpan(0, length);
        }
    }
}
