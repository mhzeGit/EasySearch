// esearch: index one or more folders and run queries against them from the terminal.
// Handy for trying the query syntax and for measuring index and search speed.
//
//   esearch <folder> [<folder>...] [-x <excluded>]... [-q <query>]... [-n <limit>]
//
// Without -q it reads queries from standard input.

using System.Diagnostics;
using EasySearch.Core;

var roots = new List<string>();
var excluded = new List<string>();
var queries = new List<string>();
int limit = 15;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-q" when i + 1 < args.Length: queries.Add(args[++i]); break;
        case "-x" when i + 1 < args.Length: excluded.Add(args[++i]); break;
        case "-n" when i + 1 < args.Length: limit = int.Parse(args[++i]); break;
        case "-p" when i + 1 < args.Length: SearchEngine.MaxParallelism = int.Parse(args[++i]); break;
        case "-h" or "--help": roots.Clear(); i = args.Length; break;
        default: roots.Add(args[i]); break;
    }
}

if (roots.Count == 0)
{
    Console.WriteLine("usage: esearch <folder> [<folder>...] [-x <excluded>]... [-q <query>]... [-n <limit>]");
    return 1;
}

var timer = Stopwatch.StartNew();
var index = IndexBuilder.Build(new IndexScope(roots, excluded));
Console.WriteLine($"Indexed {index.LiveCount:N0} items in {timer.Elapsed.TotalSeconds:F2} s " +
                  $"({GC.GetTotalMemory(forceFullCollection: true) / (1024.0 * 1024.0):F0} MB in memory)");

if (queries.Count > 0)
{
    foreach (var query in queries) Run(query);
    return 0;
}

Console.Write("> ");
while (Console.ReadLine() is { } line)
{
    if (line.Trim().Length > 0) Run(line);
    Console.Write("> ");
}
return 0;

void Run(string query)
{
    var request = new SearchRequest { Query = query, Limit = limit, ShowHidden = true, ShowSystem = true };
    // Best of a few runs, so the timing reflects steady state rather than warm-up.
    var result = SearchEngine.Run(index, request);
    for (int run = 0; run < 6 && result.Error is null; run++)
    {
        var again = SearchEngine.Run(index, request);
        if (again.ElapsedMs < result.ElapsedMs) result = again;
    }

    Console.WriteLine();
    if (result.Error is not null)
    {
        Console.WriteLine($"  {query}: {result.Error}");
        return;
    }

    Console.WriteLine($"  {query}  ->  {result.Total:N0} results in {result.ElapsedMs:F1} ms");
    foreach (var hit in result.Items)
    {
        var size = hit.IsDir ? "<dir>" : hit.Size.ToString("N0");
        Console.WriteLine($"  {size,16}  {hit.Path}");
    }
}
