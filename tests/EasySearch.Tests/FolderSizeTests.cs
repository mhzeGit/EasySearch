using EasySearch.Core;

namespace EasySearch.Tests;

public sealed class FolderSizeTests
{
    [Fact]
    public void Totals_survive_changes_subtree_deletion_compaction_and_cache_reload()
    {
        var index = new FileIndex("sizes");
        int root = index.AddRoot(@"C:\sizes");
        int folder = Add(index, root, "folder", 0, EntryFlags.Directory);
        int nested = Add(index, folder, "nested", 0, EntryFlags.Directory);
        Add(index, nested, "data.bin", 200);
        Add(index, folder, "direct.bin", 100);
        Assert.Equal(300, index.Snapshot().Size[root]);
        Assert.Equal(300, index.Snapshot().Size[folder]);
        Assert.Equal(200, index.Snapshot().Size[nested]);
        var hits = SearchEngine.Run(index, new SearchRequest { Query = "type:folder size:>150b" }).Items;
        Assert.Contains(hits, hit => hit.Name == "nested" && hit.Size == 200);
        Assert.DoesNotContain(SearchEngine.Run(index, new SearchRequest { Query = "type:folder size:<150b" }).Items, hit => hit.Name == "nested");
        var update = new PendingChange { Parent = nested, Name = "data.bin", FullPath = @"C:\sizes\folder\nested\data.bin", Exists = true, Size = 400 };
        index.Apply([update]);
        index.Apply([update]); // duplicate must not double count
        Assert.Equal(500, index.Snapshot().Size[root]);
        index.Apply([new PendingChange { Parent = root, Name = "folder", FullPath = @"C:\sizes\folder", Exists = true, Flags = EntryFlags.Directory }], out bool changed);
        Assert.False(changed); // directory metadata notifications must preserve totals
        Assert.Equal(500, index.Snapshot().Size[folder]);
        var cache = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".idx");
        try
        {
            var compact = index.Compact();
            Assert.Equal(500, compact.Snapshot().Size[0]);
            compact.Save(cache);
            Assert.Equal(500, FileIndex.TryLoad(cache, "sizes")!.Snapshot().Size[0]);
        }
        finally { File.Delete(cache); }
        index.Apply([new PendingChange { Parent = folder, Name = "nested", FullPath = @"C:\sizes\folder\nested" }]);
        Assert.Equal(100, index.Snapshot().Size[root]);
        Assert.Equal(100, index.Snapshot().Size[folder]);
        index.Apply([new PendingChange { Parent = folder, Name = "direct.bin", FullPath = @"C:\sizes\folder\direct.bin" }]);
        Assert.Equal(0, index.Snapshot().Size[root]);
        Assert.Contains(SearchEngine.Run(index, new SearchRequest { Query = "type:folder size:empty" }).Items, hit => hit.Name == "folder");
    }

    [Fact]
    public void Scanning_reconciliation_and_root_refresh_do_not_double_count()
    {
        string root = Path.Combine(Path.GetTempPath(), "EasySearchSizes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        File.WriteAllBytes(Path.Combine(root, "nested", "file.bin"), new byte[1234]);
        try
        {
            var scope = new IndexScope([root], []);
            var index = IndexBuilder.Build(scope);
            Assert.Equal(1234, index.Snapshot().Size[0]);
            var reader = new FolderReader(scope);
            var location = index.Roots()[0];
            reader.Read(location.Path);
            index.SyncFolders([reader.ToSnapshot(location.Index)], out bool changed);
            Assert.Equal(1234, index.Snapshot().Size[0]);
            File.WriteAllBytes(Path.Combine(root, "nested", "file.bin"), new byte[4321]);
            int nestedIndex = Enumerable.Range(0, index.Count).Single(i => index.Snapshot().Name(i).SequenceEqual("nested"));
            reader.Read(index.Snapshot().GetPath(nestedIndex));
            index.SyncFolders([reader.ToSnapshot(nestedIndex)], out changed);
            Assert.True(changed);
            Assert.Equal(4321, index.Snapshot().Size[0]);
            index.ReplaceRoot(location.Path, IndexBuilder.BuildRoot(scope, location.Path));
            Assert.Equal(4321, index.Snapshot().Size[0]);
            Assert.Equal(4321, index.Compact().Snapshot().Size[0]);
        }
        finally { Directory.Delete(root, true); }
    }

    static int Add(FileIndex index, int parent, string name, long size, EntryFlags flags = EntryFlags.None)
    {
        int id = index.Count;
        index.Apply([new PendingChange { Parent = parent, Name = name, FullPath = name, Exists = true, Size = size, Flags = flags }]);
        return id;
    }
}
