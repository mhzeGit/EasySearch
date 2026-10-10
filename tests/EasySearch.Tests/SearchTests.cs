using EasySearch.Core;

namespace EasySearch.Tests;

/// <summary>Builds a small real folder tree on disk and runs the index and search engine against it.</summary>
public sealed class SearchTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "EasySearchTests", Guid.NewGuid().ToString("N"));
    readonly FileIndex _index;

    public SearchTests()
    {
        Write(@"docs\Annual Report 2025.pdf", 2_000_000, daysOld: 400);
        Write(@"docs\annual report draft.docx", 40_000, daysOld: 2);
        Write(@"docs\notes.txt", 120, daysOld: 0);
        Write(@"photos\IMG_0001.JPG", 3_000_000, daysOld: 30);
        Write(@"photos\IMG_0002.jpg", 3_500_000, daysOld: 31);
        Write(@"photos\holiday.png", 900_000, daysOld: 5);
        Write(@"src\app\Program.cs", 800, daysOld: 1);
        Write(@"src\app\README.md", 300, daysOld: 1);
        Write(@"src\node_modules\left-pad\index.js", 50, daysOld: 100);
        Write(@"src\empty.log", 0, daysOld: 3);
        Write(@".cache\report-cache.bin", 10, daysOld: 1);
        new DirectoryInfo(Path.Combine(_root, ".cache")).Attributes |= FileAttributes.Hidden;

        _index = IndexBuilder.Build(Scope());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    IndexScope Scope() => new([_root], ["node_modules"]);

    void Write(string relative, int size, double daysOld)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTime(path, DateTime.Now.AddDays(-daysOld));
    }

    string[] Names(string query, Action<SearchRequest>? configure = null, FileIndex? index = null)
    {
        var request = new SearchRequest { Query = query, Sort = "name" };
        configure?.Invoke(request);
        var result = SearchEngine.Run(index ?? _index, request);
        Assert.Null(result.Error);
        Assert.Equal(result.Items.Count, result.Total);
        return [.. result.Items.Select(i => i.Name)];
    }

    [Fact]
    public void Words_are_anded_and_case_insensitive()
    {
        Assert.Equal(["Annual Report 2025.pdf", "annual report draft.docx"], Names("REPORT annual"));
        Assert.Equal(["annual report draft.docx"], Names("report draft"));
        Assert.Equal(["Annual Report 2025.pdf"], Names("Report", r => r.MatchCase = true));
    }

    [Fact]
    public void Or_not_phrase_and_wildcards()
    {
        Assert.Equal(["holiday.png", "notes.txt"], Names("holiday|notes"));
        Assert.Equal(["Annual Report 2025.pdf"], Names("report !draft"));
        Assert.Equal(["annual report draft.docx"], Names("\"report draft\""));
        Assert.Equal(["IMG_0001.JPG", "IMG_0002.jpg"], Names("img_*.jpg"));
        Assert.Equal(["IMG_0001.JPG", "IMG_0002.jpg"], Names(@"regex:^IMG_\d{4}\.jpe?g$"));
    }

    [Fact]
    public void Extension_and_type_filters()
    {
        Assert.Equal(["holiday.png", "IMG_0001.JPG", "IMG_0002.jpg"], Names("type:image"));
        Assert.Equal(["Annual Report 2025.pdf", "annual report draft.docx"], Names("ext:pdf;docx"));
        // The index location itself is a result too (so "C:\" can be found); it shows with its full path.
        Assert.Equal(["app", "docs", "photos", "src"], Names("type:folder").Where(n => !Path.IsPathRooted(n)));
        Assert.Contains(_root, Names("type:folder"));
        Assert.Equal(["holiday.png"], Names("", r => { r.Kind = "image"; r.Query = "!img"; }));
        Assert.Equal(["notes.txt"], Names("type:document !ext:pdf,docx,md"));
    }

    [Fact]
    public void Size_and_date_filters()
    {
        Assert.Equal(["Annual Report 2025.pdf", "IMG_0001.JPG", "IMG_0002.jpg"], Names("size:>1mb"));
        Assert.Equal(["annual report draft.docx", "holiday.png"], Names("size:10kb..1mb"));
        Assert.Equal(["empty.log"], Names("size:empty"));
        Assert.Equal(["notes.txt"], Names("dm:today type:file"));
        Assert.Equal(["annual report draft.docx", "notes.txt"], Names("dm:7d ext:docx|txt"));
        Assert.Equal(["Annual Report 2025.pdf"], Names($"dm:<{DateTime.Now.AddDays(-200):yyyy-MM-dd} type:file"));
        Assert.Equal(["IMG_0002.jpg"], Names("", r => { r.SizeMin = 3_200_000; r.Kind = "image"; }));
    }

    [Fact]
    public void Path_and_scope()
    {
        Assert.Equal(["Program.cs", "README.md"], Names(@"src\app\ type:file"));
        Assert.Equal(["Program.cs"], Names(@"path:app .cs"));
        Assert.Equal(["app", "empty.log", "Program.cs", "README.md"], Names($"in:\"{Path.Combine(_root, "src")}\""));
        Assert.Equal(["IMG_0001.JPG", "IMG_0002.jpg"], Names("img", r => r.Scope = Path.Combine(_root, "photos")));
        Assert.Empty(Names("img", r => r.Scope = Path.Combine(_root, "docs")));
    }

    [Fact]
    public void Excluded_folders_are_not_indexed_and_hidden_ones_are_opt_in()
    {
        Assert.Empty(Names("left-pad"));
        Assert.Empty(Names("index.js"));
        Assert.Empty(Names("report-cache"));
        Assert.Equal(["report-cache.bin"], Names("report-cache", r => r.ShowHidden = true));
    }

    [Fact]
    public void Best_match_ranks_exact_and_prefix_matches_first()
    {
        Write(@"src\app\notes\deep\my notes backup.txt", 10, daysOld: 0);
        var index = IndexBuilder.Build(Scope());
        var result = SearchEngine.Run(index, new SearchRequest { Query = "notes" });
        Assert.Equal(["notes", "notes.txt", "my notes backup.txt"], result.Items.Select(i => i.Name));
    }

    [Fact]
    public void Sorting_and_limit()
    {
        var bySize = SearchEngine.Run(_index, new SearchRequest { Query = "type:file", Sort = "size", Desc = true, Limit = 2 });
        Assert.Equal(["IMG_0002.jpg", "IMG_0001.JPG"], bySize.Items.Select(i => i.Name));
        Assert.Equal(9, bySize.Total);

        var newest = SearchEngine.Run(_index, new SearchRequest { Query = "type:file", Limit = 1 });
        Assert.Equal("notes.txt", newest.Items[0].Name); // filters only: newest first
    }

    [Fact]
    public void Bad_syntax_is_reported_not_thrown()
    {
        Assert.NotNull(SearchEngine.Run(_index, new SearchRequest { Query = "size:huge-ish" }).Error);
        Assert.NotNull(SearchEngine.Run(_index, new SearchRequest { Query = "regex:([a-z" }).Error);
        Assert.NotNull(SearchEngine.Run(_index, new SearchRequest { Query = "type:spreadsheets" }).Error);
    }

    [Fact]
    public void Results_carry_full_paths()
    {
        var hit = Assert.Single(SearchEngine.Run(_index, new SearchRequest { Query = "program.cs" }).Items);
        Assert.Equal(Path.Combine(_root, @"src\app"), hit.Dir);
        Assert.Equal(Path.Combine(_root, @"src\app\Program.cs"), hit.Path);
        Assert.Equal(800, hit.Size);
        Assert.False(hit.IsDir);
    }

    [Fact]
    public void Cache_round_trip_preserves_the_index()
    {
        var file = Path.Combine(_root, "index.bin");
        _index.Save(file);

        var loaded = FileIndex.TryLoad(file, Scope().Signature);
        Assert.NotNull(loaded);
        Assert.Equal(_index.LiveCount, loaded.LiveCount);
        Assert.Equal(Names("report"), Names("report", index: loaded));
        Assert.Equal(Path.Combine(_root, "docs"), loaded.Snapshot().GetPath(loaded.Resolve(Path.Combine(_root, "docs"))));

        Assert.Null(FileIndex.TryLoad(file, "another scope"));
    }

    [Fact]
    public void Live_changes_are_picked_up()
    {
        using var service = new IndexService(Path.Combine(_root, "live.bin"));
        service.Start(Scope());
        WaitFor(() => !service.Status.Indexing && service.Index.LiveCount > 0);

        string[] Live(string query) => Names(query, index: service.Index);

        Write(@"docs\fresh-invoice.pdf", 10, daysOld: 0);
        WaitFor(() => Live("fresh-invoice").Length == 1);

        File.Move(Path.Combine(_root, @"docs\fresh-invoice.pdf"), Path.Combine(_root, @"docs\paid-invoice.pdf"));
        WaitFor(() => Live("paid-invoice").Length == 1 && Live("fresh-invoice").Length == 0);

        // A folder that appears with content already inside it gets scanned.
        var staging = Path.Combine(Path.GetDirectoryName(_root)!, "staging-" + Path.GetFileName(_root));
        Directory.CreateDirectory(Path.Combine(staging, "inner"));
        File.WriteAllText(Path.Combine(staging, @"inner\moved-in.txt"), "x");
        Directory.Move(staging, Path.Combine(_root, "arrived"));
        WaitFor(() => Live("moved-in").Length == 1);

        Directory.Move(Path.Combine(_root, "arrived"), Path.Combine(_root, "renamed"));
        WaitFor(() => Live(@"renamed\ moved-in").Length == 1 && Live("arrived").Length == 0);

        Directory.Delete(Path.Combine(_root, "photos"), recursive: true);
        WaitFor(() => Live("img_").Length == 0 && Live("photos").Length == 0);

        // Excluded folders stay out even when they are created later.
        Write(@"docs\node_modules\pkg\late.js", 1, daysOld: 0);
        Write(@"docs\marker.txt", 1, daysOld: 0);
        WaitFor(() => Live("marker.txt").Length == 1);
        Assert.Empty(Live("late.js"));
    }

    static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the index to catch up");
            Thread.Sleep(50);
        }
    }
}
