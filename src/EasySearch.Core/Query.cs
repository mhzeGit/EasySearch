using System.Globalization;
using System.Text.RegularExpressions;

namespace EasySearch.Core;

public sealed class SearchRequest
{
    public string Query { get; set; } = "";
    /// <summary>all, folder, file, or one of <see cref="FileKinds.Names"/>.</summary>
    public string Kind { get; set; } = "all";
    /// <summary>
    /// Exact file suffixes to require on top of <see cref="Kind"/> (the custom type filter): "pdf",
    /// ".pdf .docx" or "pdf;docx". A leading dot or <c>*.</c> is ignored.
    /// </summary>
    public string Extensions { get; set; } = "";
    public long? SizeMin { get; set; }
    public long? SizeMax { get; set; }
    /// <summary>Unix milliseconds.</summary>
    public long? ModifiedAfter { get; set; }
    public long? ModifiedBefore { get; set; }
    public string? Scope { get; set; }
    /// <summary>relevance, name, path, size or modified.</summary>
    public string Sort { get; set; } = "relevance";
    public bool Desc { get; set; }
    public int Limit { get; set; } = 200;
    public bool MatchCase { get; set; }
    public bool MatchPath { get; set; }
    public bool Regex { get; set; }
    public bool ShowHidden { get; set; }
    public bool ShowSystem { get; set; }
}

enum MatchKind { Substring, ExactName, Wildcard, Regex }

sealed class Alternative
{
    public MatchKind Kind;
    public string Text = "";
    public Regex? Regex;
    /// <summary>Set when a pathological pattern hit the match timeout; the pattern is then skipped.</summary>
    public volatile bool TimedOut;
}

/// <summary>One AND-ed part of the query; it matches when any of its alternatives does.</summary>
sealed class Term
{
    public bool Negate;
    public bool OnPath;
    public Alternative[] Alternatives = [];
}

sealed class CompiledQuery
{
    public readonly List<Term> Terms = [];
    public readonly List<HashSet<string>> RequiredExtensions = [];
    public HashSet<string>? ForbiddenExtensions;
    public bool FoldersOnly, FilesOnly;
    public bool HasSize, HasDate;
    public long SizeMin, SizeMax = long.MaxValue;
    public long ModifiedMin = long.MinValue, ModifiedMax = long.MaxValue; // [min, max) in UTC ticks
    public string? Scope;
    public readonly List<string> Inside = [];
    public bool IgnoreCase = true;
    public bool ShowHidden, ShowSystem;
    /// <summary>The term whose match position drives relevance ranking, or -1.</summary>
    public int Primary = -1;

    public StringComparison Comparison => IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

/// <summary>
/// Turns the query text plus the UI filters into <see cref="CompiledQuery"/> branches.
/// Syntax: words are AND-ed, <c>a|b</c> or <c>a OR b</c> is OR (see <see cref="QueryLogic"/> for
/// AND, NOT and parentheses), <c>!word</c> negates, <c>"quoted phrase"</c>,
/// <c>*</c>/<c>?</c> wildcards, and operators such as <c>ext:</c>, <c>size:</c>, <c>dm:</c>,
/// <c>type:</c>, <c>path:</c>, <c>in:</c> and <c>regex:</c>.
/// </summary>
static class QueryParser
{
    /// <summary>
    /// One compiled query per OR branch; a document matches when any branch matches. Without OR or
    /// parentheses (the usual case) there is exactly one.
    /// </summary>
    public static List<CompiledQuery> Parse(SearchRequest request, DateTime now)
    {
        var text = request.Query.Trim();
        if (request.Regex)
        {
            var q = NewBranch(request);
            if (text.Length > 0)
                q.Terms.Add(new Term { OnPath = request.MatchPath, Alternatives = [RegexAlternative(text, q.IgnoreCase)] });
            return [Finish(q)];
        }

        var branches = new List<CompiledQuery>();
        foreach (var tokens in QueryLogic.Split(text))
        {
            var q = NewBranch(request);
            foreach (var token in tokens) ParseToken(q, token, request.MatchPath, now);
            branches.Add(Finish(q));
        }
        return branches;
    }

    /// <summary>A branch starts with everything the UI filters ask for; the query text adds to it.</summary>
    static CompiledQuery NewBranch(SearchRequest request)
    {
        var q = new CompiledQuery
        {
            IgnoreCase = !request.MatchCase,
            ShowHidden = request.ShowHidden,
            ShowSystem = request.ShowSystem,
        };

        ApplyKind(q, request.Kind, negate: false);
        var suffixes = ParseExtensions(request.Extensions);
        if (suffixes.Count > 0) q.RequiredExtensions.Add(suffixes);
        if (request.SizeMin is { } min) { q.HasSize = true; q.SizeMin = Math.Max(q.SizeMin, min); }
        if (request.SizeMax is { } max) { q.HasSize = true; q.SizeMax = Math.Min(q.SizeMax, max); }
        if (request.ModifiedAfter is { } after) { q.HasDate = true; q.ModifiedMin = PathUtil.FromUnixMs(after); }
        if (request.ModifiedBefore is { } before) { q.HasDate = true; q.ModifiedMax = PathUtil.FromUnixMs(before); }
        if (!string.IsNullOrWhiteSpace(request.Scope)) q.Scope = request.Scope;
        return q;
    }

    static CompiledQuery Finish(CompiledQuery q)
    {
        q.Primary = q.Terms.FindIndex(t =>
            !t.Negate && !t.OnPath && t.Alternatives.Length == 1 && t.Alternatives[0].Kind == MatchKind.Substring);
        return q;
    }

    static void ParseToken(CompiledQuery q, string token, bool matchPath, DateTime now)
    {
        bool negate = token.Length > 1 && token[0] == '!';
        if (negate) token = token[1..];

        int colon = token.IndexOf(':');
        if (colon > 0)
        {
            var key = token[..colon].ToLowerInvariant();
            var value = token[(colon + 1)..];
            switch (key)
            {
                case "ext":
                    var extensions = ParseExtensions(value);
                    if (negate) (q.ForbiddenExtensions ??= NewSet()).UnionWith(extensions);
                    else q.RequiredExtensions.Add(extensions);
                    return;

                case "type" or "t" or "kind":
                    ApplyKind(q, value, negate);
                    return;

                case "folder" or "folders" or "file" or "files":
                    ApplyKind(q, key, negate);
                    if (value.Length > 0) AddTextTerm(q, value, negate: false, onPath: matchPath, forceName: false);
                    return;

                case "size":
                    if (negate) throw NotUnsupported(key);
                    if (!TryParseSize(value, ref q.SizeMin, ref q.SizeMax))
                        throw new FormatException($"Can't read size \"{value}\". Try size:>10mb or size:1mb..1gb");
                    q.HasSize = true;
                    return;

                case "date" or "dm" or "modified" or "datemodified" or "changed" or "updated":
                    if (negate) throw NotUnsupported(key);
                    if (!TryParseDate(value, now, ref q.ModifiedMin, ref q.ModifiedMax))
                        throw new FormatException($"Can't read date \"{value}\". Try date:today, date:7d or date:>2026-01-01");
                    q.HasDate = true;
                    return;

                case "in":
                    if (negate) throw NotUnsupported(key);
                    if (value.Length > 0)
                    {
                        if (value.IndexOfAny(['\\', '/', ':']) >= 0) q.Scope = value;
                        else q.Inside.Add(value);
                    }
                    return;

                case "source" or "location" or "where" or "under" or "parent":
                    if (negate) throw NotUnsupported(key);
                    if (value.Length > 0) q.Scope = value;
                    return;

                case "inside" or "foldername" or "folder-name":
                    if (negate) throw NotUnsupported(key);
                    if (value.Length > 0) q.Inside.Add(value);
                    return;

                case "path":
                    if (value.Length > 0) AddTextTerm(q, value, negate, onPath: true, forceName: false);
                    return;

                case "name":
                    if (value.Length > 0) AddTextTerm(q, value, negate, onPath: false, forceName: true);
                    return;

                case "regex":
                    if (value.Length > 0)
                        q.Terms.Add(new Term { Negate = negate, OnPath = matchPath, Alternatives = [RegexAlternative(value, q.IgnoreCase)] });
                    return;
            }
        }

        // Not an operator ("C:\Users" lands here too): plain text.
        AddTextTerm(q, token, negate, matchPath, forceName: false);
    }

    static FormatException NotUnsupported(string key) =>
        new($"NOT can't be used with {key}:. Describe the range you want instead, e.g. size:<1mb or date:>2026-01-01");

    static void AddTextTerm(CompiledQuery q, string text, bool negate, bool onPath, bool forceName)
    {
        var parts = text.Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        var term = new Term { Negate = negate, OnPath = onPath, Alternatives = new Alternative[parts.Length] };
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Replace('/', '\\');
            if (!forceName && part.Contains('\\')) term.OnPath = true;
            bool wildcard = part.Contains('*') || part.Contains('?');
            term.Alternatives[i] = new Alternative { Kind = wildcard ? MatchKind.Wildcard : MatchKind.Substring, Text = part };
        }
        q.Terms.Add(term);
    }

    static Alternative RegexAlternative(string pattern, bool ignoreCase)
    {
        var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        try
        {
            // Names are short, so the plain interpreter is the fastest engine per call. The timeout
            // guards against pathological patterns (see Alternative.TimedOut).
            var regex = new Regex(pattern, options, TimeSpan.FromMilliseconds(50));
            return new Alternative { Kind = MatchKind.Regex, Text = pattern, Regex = regex };
        }
        catch (ArgumentException e)
        {
            throw new FormatException("Invalid regex: " + e.Message);
        }
    }

    static void ApplyKind(CompiledQuery q, string value, bool negate)
    {
        var required = NewSet();
        foreach (var kind in SplitList(value))
        {
            switch (kind.ToLowerInvariant())
            {
                case "" or "all" or "any" or "everything":
                    break;
                case var suffix when suffix.StartsWith('.') || suffix.StartsWith("*."):
                    // type:.pdf -- a literal file suffix instead of a named kind
                    var literal = ParseExtensions(suffix);
                    if (negate) (q.ForbiddenExtensions ??= NewSet()).UnionWith(literal);
                    else required.UnionWith(literal);
                    break;
                case "folder" or "folders" or "dir" or "directory":
                    if (negate) q.FilesOnly = true; else q.FoldersOnly = true;
                    break;
                case "file" or "files":
                    if (negate) q.FoldersOnly = true; else q.FilesOnly = true;
                    break;
                default:
                    // A kind or tool name (image, 3d, blender ...), or else a bare suffix: type:fbx is type:.fbx.
                    var extensions = FileKinds.Extensions(kind);
                    if (extensions is null)
                    {
                        if (kind.IndexOfAny(['\\', '/', ':', '"', '<', '>', '?', '*']) >= 0)
                            throw new FormatException($"Can't read type \"{kind}\". Use a name such as image, 3d or blender, or a suffix such as pdf or .fbx");
                        extensions = new HashSet<string>([kind], StringComparer.OrdinalIgnoreCase);
                    }
                    if (negate) (q.ForbiddenExtensions ??= NewSet()).UnionWith(extensions);
                    else required.UnionWith(extensions);
                    break;
            }
        }
        if (required.Count > 0) q.RequiredExtensions.Add(required);
    }

    static HashSet<string> NewSet() => new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"pdf", ".pdf", "*.pdf", or a list separated by space, comma, semicolon or bar.</summary>
    static HashSet<string> ParseExtensions(string value)
    {
        var set = NewSet();
        foreach (var part in value.Split([';', ',', '|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var extension = part.TrimStart('*', '.');
            if (extension.Length > 0) set.Add(extension);
        }
        return set;
    }

    static string[] SplitList(string value) =>
        value.Split([';', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ---- size ---------------------------------------------------------------------------

    internal static bool TryParseSize(string value, ref long min, ref long max)
    {
        const long KB = 1024, MB = KB * 1024;
        long lo = 0, hi = long.MaxValue;
        switch (value.ToLowerInvariant())
        {
            case "empty": hi = 0; break;
            case "tiny": hi = 10 * KB; break;
            case "small": lo = 10 * KB; hi = 100 * KB; break;
            case "medium": lo = 100 * KB; hi = MB; break;
            case "large": lo = MB; hi = 16 * MB; break;
            case "huge": lo = 16 * MB; hi = 128 * MB; break;
            case "gigantic": lo = 128 * MB; break;
            default:
                int range = value.IndexOf("..", StringComparison.Ordinal);
                if (range >= 0)
                {
                    if (range > 0 && !TryParseBytes(value[..range], out lo)) return false;
                    if (range + 2 < value.Length && !TryParseBytes(value[(range + 2)..], out hi)) return false;
                }
                else if (value.StartsWith(">=")) { if (!TryParseBytes(value[2..], out lo)) return false; }
                else if (value.StartsWith("<=")) { if (!TryParseBytes(value[2..], out hi)) return false; }
                else if (value.StartsWith('>')) { if (!TryParseBytes(value[1..], out lo)) return false; lo++; }
                else if (value.StartsWith('<')) { if (!TryParseBytes(value[1..], out hi)) return false; hi--; }
                else if (value.StartsWith('=')) { if (!TryParseBytes(value[1..], out lo)) return false; hi = lo; }
                else if (TryParseDashRange(value, out lo, out hi)) { } // size:1mb-10mb
                else if (!TryParseBytes(value, out lo)) return false; // bare "size:10mb" means "at least"
                break;
        }
        min = Math.Max(min, lo);
        max = Math.Min(max, hi);
        return true;
    }

    /// <summary>"1mb-10mb", or "1-10mb" where the unit on the right also covers the bare number on the left.</summary>
    static bool TryParseDashRange(string value, out long lo, out long hi)
    {
        lo = 0; hi = long.MaxValue;
        int dash = value.IndexOf('-', 1);
        if (dash <= 0 || dash == value.Length - 1) return false;

        var left = value[..dash].Trim();
        var right = value[(dash + 1)..].Trim();
        if (left.Length > 0 && char.IsAsciiDigit(left[^1]) && right.Length > 0 && !char.IsAsciiDigit(right[^1]))
        {
            int unit = right.Length;
            while (unit > 0 && char.IsAsciiLetter(right[unit - 1])) unit--;
            left += right[unit..];
        }
        if (!TryParseBytes(left, out lo) || !TryParseBytes(right, out hi)) return false;
        if (lo > hi) (lo, hi) = (hi, lo);
        return true;
    }

    static bool TryParseBytes(string text, out long bytes)
    {
        bytes = 0;
        text = text.Trim().ToLowerInvariant();
        int digits = 0;
        while (digits < text.Length && (char.IsAsciiDigit(text[digits]) || text[digits] == '.')) digits++;
        if (digits == 0 || !double.TryParse(text[..digits], NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            return false;

        double unit;
        switch (text[digits..].Trim())
        {
            case "" or "b": unit = 1; break;
            case "k" or "kb": unit = 1L << 10; break;
            case "m" or "mb": unit = 1L << 20; break;
            case "g" or "gb": unit = 1L << 30; break;
            case "t" or "tb": unit = 1L << 40; break;
            default: return false;
        }
        bytes = (long)Math.Min(number * unit, long.MaxValue / 2);
        return true;
    }

    // ---- dates --------------------------------------------------------------------------

    internal static bool TryParseDate(string value, DateTime now, ref long min, ref long max)
    {
        var today = now.Date;
        DateTime? from = null, to = null;
        var text = value.ToLowerInvariant();

        switch (text)
        {
            case "today": from = today; to = today.AddDays(1); break;
            case "yesterday": from = today.AddDays(-1); to = today; break;
            case "week" or "thisweek": from = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); break;
            case "month" or "thismonth": from = new DateTime(today.Year, today.Month, 1); break;
            case "year" or "thisyear": from = new DateTime(today.Year, 1, 1); break;
            default:
                var relative = Regex.Match(text, @"^(\d{1,5})(h|d|w|m|y)$");
                int range = text.IndexOf("..", StringComparison.Ordinal);
                if (relative.Success)
                {
                    int n = int.Parse(relative.Groups[1].Value, CultureInfo.InvariantCulture);
                    from = relative.Groups[2].Value switch
                    {
                        "h" => now.AddHours(-n),
                        "d" => now.AddDays(-n),
                        "w" => now.AddDays(-7.0 * n),
                        "m" => now.AddMonths(-Math.Min(n, 1200)),
                        _ => now.AddYears(-Math.Min(n, 100)),
                    };
                }
                else if (range >= 0)
                {
                    if (range > 0) { if (!TryParsePeriod(text[..range], out var a, out _)) return false; from = a; }
                    if (range + 2 < text.Length) { if (!TryParsePeriod(text[(range + 2)..], out _, out var b)) return false; to = b; }
                }
                else if (text.StartsWith("<="))
                {
                    if (!TryParsePeriod(text[2..], out _, out var end)) return false;
                    to = end;
                }
                else if (text.StartsWith('>') || text.StartsWith('<'))
                {
                    bool after = text[0] == '>';
                    if (!TryParsePeriod(text.TrimStart('>', '<', '='), out var start, out _)) return false;
                    if (after) from = start; else to = start;
                }
                else
                {
                    if (!TryParsePeriod(text, out var start, out var end)) return false;
                    from = start; to = end;
                }
                break;
        }

        if (from is { } f) min = Math.Max(min, ToUtcTicks(f));
        if (to is { } t) max = Math.Min(max, ToUtcTicks(t));
        return true;
    }

    /// <summary>"2026", "2026-03" or "2026-03-14" as a [start, end) period in local time.</summary>
    static bool TryParsePeriod(string text, out DateTime start, out DateTime end)
    {
        start = end = default;
        var parts = text.Trim().Split('-', '/');
        if (parts.Length is < 1 or > 3) return false;

        var numbers = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;

        try
        {
            switch (numbers.Length)
            {
                case 1: start = new DateTime(numbers[0], 1, 1); end = start.AddYears(1); break;
                case 2: start = new DateTime(numbers[0], numbers[1], 1); end = start.AddMonths(1); break;
                default: start = new DateTime(numbers[0], numbers[1], numbers[2]); end = start.AddDays(1); break;
            }
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    static long ToUtcTicks(DateTime local) => DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime().Ticks;
}
