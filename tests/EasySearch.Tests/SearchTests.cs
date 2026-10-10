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
    public void Empty_query_returns_everything_and_text_keeps_the_full_word()
    {
        Write(@"docs\forklift.txt", 12, daysOld: 0);
        Write(@"docs\fork-fork-fork.txt", 12, daysOld: 0);
        Write(@"docs\f.txt", 12, daysOld: 0);
        var index = IndexBuilder.Build(Scope());

        var all = SearchEngine.Run(index, new SearchRequest { Limit = 1000, Sort = "name" });
        Assert.Null(all.Error);
        Assert.Contains(all.Items, item => item.Name == "Annual Report 2025.pdf");
        Assert.Contains(all.Items, item => item.Name == "forklift.txt");

        SearchResult? partial = null;
        var forks = SearchEngine.Run(index, new SearchRequest { Query = "fork", Sort = "name" }, onProgress: result => partial = result);
        Assert.Equal(["fork-fork-fork.txt", "forklift.txt"], forks.Items.Select(item => item.Name));
        Assert.NotNull(partial);
        Assert.Equal(["fork-fork-fork.txt", "forklift.txt"], partial.Items.Select(item => item.Name));
        var folders = SearchEngine.Run(index, new SearchRequest { Query = "type:folder", Limit = 1000 });
        Assert.Null(folders.Error);
        Assert.NotEmpty(folders.Items);
        Assert.All(folders.Items, item => Assert.True(item.IsDir));
        Assert.True(folders.Total < all.Total);
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
    public void Inside_matches_any_parent_folder_with_the_name()
    {
        Assert.Equal(new[] { "Program.cs", "README.md" }, Names("inside:app"));
        Assert.Equal(new[] { "notes.txt", "Annual Report 2025.pdf", "annual report draft.docx" }.OrderBy(n => n),
            Names("inside:docs").OrderBy(n => n));
        Assert.Equal(["Program.cs", "README.md"], Names("in:" + Path.Combine(_root, "src", "app")));
    }

    [Fact]
    public void Multiple_inside_filters_require_all_parent_folders_in_either_order()
    {
        Write(@"blahblahfamily\unity\deep\nested.txt", 1, daysOld: 0);
        Write(@"unity\blahblahfamily\reverse.txt", 1, daysOld: 0);
        Write(@"unity\unrelated.txt", 1, daysOld: 0);
        Write(@"blahblahfamily\outside.txt", 1, daysOld: 0);
        var index = IndexBuilder.Build(Scope());

        Assert.Equal(["nested.txt", "reverse.txt"],
            Names("inside:blahblahfamily and inside:unity type:file", index: index));
        Assert.Equal(["nested.txt", "reverse.txt"],
            Names("in:unity inside:blahblahfamily type:file", index: index));
        Assert.Equal(["deep", "nested.txt", "reverse.txt"],
            Names("inside:unity inside:blahblahfamily", index: index));
        Assert.Empty(Names("inside:docs inside:photos", index: index));
        Assert.Equal(["notes.txt", "Program.cs", "README.md"],
            Names("(inside:src (inside:app OR inside:docs)) OR (inside:docs name:notes.txt)", index: index));
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
    public void Logic_words_and_friendly_ranges()
    {
        // type:.pdf is a literal suffix, same as ext:pdf.
        Assert.Equal(["Annual Report 2025.pdf"], Names("type:.pdf"));
        Assert.Equal(Names("ext:pdf;docx"), Names("type:.pdf,.docx"));
        Assert.Equal(Names("type:document !ext:pdf"), Names("type:document NOT type:.pdf"));

        // AND is implied; OR and NOT are spelled in capitals.
        Assert.Equal(["Annual Report 2025.pdf"], Names("annual AND report AND type:.pdf"));
        Assert.Equal(Names("annual|holiday"), Names("annual OR holiday"));
        Assert.Equal(Names("ext:pdf;docx"), Names("ext:pdf OR ext:docx"));
        Assert.Equal(Names("ext:pdf;docx;md"), Names("ext:pdf OR ext:docx OR ext:md"));
        Assert.Equal(["holiday.png"], Names("type:image NOT img"));
        Assert.Equal(Names("type:image NOT img"), Names("type:image AND NOT img"));
        Assert.Equal(Names("ext:png OR !ext:pdf"), Names("ext:png OR NOT ext:pdf"));
        Assert.Equal(Names("annual|holiday type:file"), Names("annual OR holiday AND type:file"));

        // A quoted word is just a word, and a dangling operator is an error.
        Assert.Equal(Names("or"), Names("\"OR\""));
        Assert.NotNull(Error("annual OR"));
        Assert.NotNull(Error("OR annual"));
        Assert.NotNull(Error("annual AND OR holiday"));
        Assert.NotNull(Error("annual NOT"));

        // size:min-max, with the unit allowed once, and in either order.
        var between = Names("size:10kb..1mb");
        Assert.Equal(between, Names("size:10kb-1mb"));
        Assert.Equal(between, Names("size:1mb-10kb"));
        Assert.Equal(Names("size:10kb..1000kb"), Names("size:10-1000kb"));
        Assert.NotNull(Error("size:abc-def"));
    }

    [Fact]
    public void Type_accepts_suffixes_without_a_dot_and_tool_names()
    {
        // A bare word that is not a known name is a suffix: type:pdf == type:.pdf == ext:pdf.
        Assert.Equal(["Annual Report 2025.pdf"], Names("type:pdf"));
        Assert.Equal(["holiday.png"], Names("type:PNG"));
        Assert.Equal(Names("type:png;.pdf"), Names("type:png,pdf"));
        Assert.Equal(["Annual Report 2025.pdf", "holiday.png"], Names("type:.pdf|png"));

        // Tool and kind names, spelled however people write them.
        Assert.Equal(Names("type:image"), Names("type:photos"));
        Assert.Equal(["annual report draft.docx"], Names("type:word"));
        Assert.Equal(["Program.cs"], Names("type:csharp"));
        Assert.Equal(["Program.cs"], Names("type:C#"));
        Assert.Empty(Names("type:blender"));
        Assert.Equal(["notes.txt"], Names("type:text !type:md,log"));

        foreach (var name in new[] { "blender", "3D", "3d models", "3d-models", "Cinema 4D" })
            Assert.NotNull(FileKinds.Extensions(name));
        Assert.Contains("blend", FileKinds.Extensions("Blender")!);
        Assert.Contains("fbx", FileKinds.Extensions("3D")!);
        Assert.Contains("blend", FileKinds.Extensions("3D models")!);
        Assert.Contains("fbx", FileKinds.Extensions("3d-models")!);
        Assert.Null(FileKinds.Extensions("fbx")); // a plain suffix, handled by the parser

        Assert.Contains(FileKinds.Catalog, t => t.Name == "blender" && t.Extensions.Contains("blend"));
    }

    [Fact]
    public void Or_between_any_filters_and_grouping()
    {
        static string[] Sorted(IEnumerable<string> names) => [.. names.Order(StringComparer.Ordinal)];

        // OR is not limited to words or one kind of filter.
        Assert.Equal(Sorted(Names("ext:pdf").Union(Names("annual"))), Sorted(Names("ext:pdf OR annual")));
        Assert.Equal(Sorted(Names("size:empty").Union(Names("size:>3mb"))), Sorted(Names("size:empty OR size:>3mb")));
        Assert.Equal(Sorted(Names("ext:png").Union(Names("size:empty"))), Sorted(Names("ext:png || size:empty")));

        // Parentheses decide what the OR covers.
        Assert.Equal(["annual report draft.docx"], Names("(ext:pdf OR ext:docx) AND draft"));
        Assert.Equal(["annual report draft.docx", "holiday.png"], Sorted(Names("ext:png OR (ext:docx AND annual)")));
        Assert.Equal(["holiday.png"], Names("ext:png OR (ext:docx AND zzz)"));
        Assert.Equal(Names("type:document !ext:pdf,docx"), Names("type:document NOT (ext:pdf OR ext:docx)"));

        // NOT over a group flips AND to OR (De Morgan): everything except "a pdf that is also annual".
        var notAnnualPdf = Names("type:document NOT (ext:pdf AND annual)");
        Assert.DoesNotContain("Annual Report 2025.pdf", notAnnualPdf);
        Assert.Contains("annual report draft.docx", notAnnualPdf);

        // Filters from the UI apply to every branch; each branch can carry its own scope.
        Assert.Equal(["IMG_0001.JPG", "IMG_0002.jpg"], Names("size:>1mb OR zzz", r => { r.Kind = "image"; r.Scope = Path.Combine(_root, "photos"); }));
        Assert.Equal(["annual report draft.docx"], Names("ext:docx OR ext:log OR ext:png", r => r.Extensions = "docx"));

        // Each branch can carry its own folder: .cs files in src, or anything png in photos.
        var src = Path.Combine(_root, "src");
        var docs = Path.Combine(_root, "docs");
        Assert.Equal(Sorted(Names($"in:\"{src}\" ext:cs").Union(Names($"in:\"{docs}\" ext:docx"))),
            Sorted(Names($"(in:\"{src}\" ext:cs) OR (in:\"{docs}\" ext:docx)")));
    }

    [Fact]
    public void Lower_case_words_and_stray_parentheses_fall_back_to_plain_text()
    {
        // and / or work in lower case, as does && ...
        Assert.Equal(Names("annual report"), Names("annual and report"));
        Assert.Equal(Names("annual OR holiday"), Names("annual or holiday"));
        Assert.Equal(Names("annual report"), Names("annual && report"));

        // ... but only while that makes sense; otherwise the words are searched for as written.
        Assert.Equal(Names("report \"or\""), Names("report or"));
        Assert.Null(Error("report or"));
        // NOT must be capitalised, so "do not disturb" can never silently drop a word.
        Assert.Empty(Names("type:image not img"));
        Assert.NotEmpty(Names("type:image NOT img"));

        // A lone parenthesis, or one wrapped round a single word, is part of a name.
        Assert.Null(Error("(annual"));
        Assert.Null(Error("annual)"));
        Assert.Empty(Names("(annual)"));
        Assert.NotEmpty(Names("(annual OR holiday)"));

        Assert.NotNull(Error("NOT size:>1mb"));

        // (a OR b) (c OR d) ... multiplies out; keep it bounded.
        var groups = string.Join(' ', Enumerable.Repeat("(ext:pdf OR size:>1mb)", 6));
        Assert.NotNull(Error(groups));
    }

    string? Error(string query) => SearchEngine.Run(_index, new SearchRequest { Query = query }).Error;

    [Fact]
    public void Custom_extension_filter()
    {
        // The custom type box: dots, wildcards, case and separators are all forgiven.
        var pdfAndDocx = Names("ext:pdf;docx");
        Assert.Equal(pdfAndDocx, Names("", r => r.Extensions = ".PDF docx"));
        Assert.Equal(pdfAndDocx, Names("", r => r.Extensions = "*.pdf, .docx"));
        Assert.Equal(["README.md"], Names("", r => { r.Kind = "document"; r.Extensions = "md"; }));
        Assert.Empty(Names("", r => { r.Kind = "image"; r.Extensions = "pdf"; }));
        Assert.Equal(["Annual Report 2025.pdf"], Names("annual", r => r.Extensions = "pdf"));
        Assert.Equal(Names("type:file"), Names("type:file", r => r.Extensions = " . "));
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
    public void Friendly_operator_names()
    {
        // date: is the plain-English name for dm:; location and source are aliases for in:.
        var today = Names("dm:today type:file");
        Assert.Equal(["notes.txt"], today);
        foreach (var name in new[] { "date", "modified", "changed", "updated", "datemodified" })
            Assert.Equal(today, Names($"{name}:today type:file"));

        var src = Path.Combine(_root, "src");
        var inside = Names($"in:\"{src}\"");
        foreach (var name in new[] { "source", "location", "where", "under" })
            Assert.Equal(inside, Names($"{name}:\"{src}\""));
        Assert.Equal(["Program.cs", "README.md"], Names("inside:app"));

        // They also join up with OR, like any other filter.
        Assert.Equal(Names("date:today OR size:empty").OrderBy(n => n), Names("dm:today").Union(Names("size:empty")).OrderBy(n => n));
        Assert.Contains("date:", Error("date:nonsense")!); // errors use the friendly name too
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
        Assert.NotNull(SearchEngine.Run(_index, new SearchRequest { Query = @"type:a\b" }).Error);
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

    /// <summary>Once with the NTFS change journal, once with file system notifications (the fallback for other drives).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Live_changes_are_picked_up(bool useJournal)
    {
        var cache = Path.Combine(Path.GetTempPath(), "EasySearchTests", Path.GetFileName(_root) + "-live.bin");
        using var service = new IndexService(cache, useJournal);
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

        Assert.Equal(1, service.FullScans); // all of the above without scanning again
        Assert.Equal(0, service.LocationScans);
        File.Delete(cache);
    }

    [Fact]
    public void Restart_catches_up_from_the_journal_without_rescanning()
    {
        var cache = Path.Combine(Path.GetTempPath(), "EasySearchTests", Path.GetFileName(_root) + "-restart.bin");
        using (var first = new IndexService(cache))
        {
            first.Start(Scope());
            WaitFor(() => !first.Status.Indexing && first.Index.LiveCount > 0);
            Assert.Equal(1, first.FullScans);
        }

        // While the app is "closed": add, change, rename and delete things.
        Write(@"docs\written-while-closed.txt", 5, daysOld: 0);
        Write(@"new-folder\deep\inside.md", 5, daysOld: 0);
        File.WriteAllBytes(Path.Combine(_root, @"docs\notes.txt"), new byte[4321]);
        File.Move(Path.Combine(_root, @"photos\holiday.png"), Path.Combine(_root, @"photos\beach.png"));
        File.Delete(Path.Combine(_root, @"src\app\README.md"));
        Directory.Delete(Path.Combine(_root, @"src\app"), recursive: true);

        using var second = new IndexService(cache);
        second.Start(Scope());
        string[] Live(string query) => Names(query, index: second.Index);
        WaitFor(() => !second.Status.Indexing && Live("written-while-closed").Length == 1);

        Assert.Equal(["inside.md"], Live(@"new-folder\deep\ type:file"));
        Assert.Equal(["notes.txt"], Live("notes size:4321..4321"));
        Assert.Equal(["beach.png"], Live("beach|holiday"));
        Assert.Empty(Live("program.cs|readme.md"));
        Assert.Empty(Live(@"path:src\app"));
        Assert.Equal(0, second.FullScans);
        Assert.Equal(0, second.LocationScans);
        File.Delete(cache);
    }

    [Fact]
    public void A_location_can_be_refreshed_in_place_and_the_index_compacted()
    {
        int before = _index.LiveCount;
        Write(@"docs\added-later.txt", 1, daysOld: 0);
        File.Delete(Path.Combine(_root, @"photos\holiday.png"));

        var root = PathUtil.Normalize(_root);
        _index.ReplaceRoot(root, IndexBuilder.BuildRoot(Scope(), root));
        Assert.Equal(before, _index.LiveCount); // one added, one removed
        Assert.Equal(["added-later.txt"], Names("added-later"));
        Assert.Empty(Names("holiday"));
        Assert.Equal(["Program.cs"], Names(@"src\app\ .cs"));

        var compact = _index.Compact();
        Assert.Equal(0, compact.DeadCount);
        Assert.Equal(before, compact.LiveCount);
        Assert.Equal(Names("type:file"), Names("type:file", index: compact));
        Assert.Equal(Names("report-cache", r => r.ShowHidden = true), Names("report-cache", r => r.ShowHidden = true, index: compact));
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
