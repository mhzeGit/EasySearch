using EasySearch.Core;

namespace EasySearch.Tests;

public sealed class IndexChangeTests
{
    [Fact]
    public void Duplicate_notifications_and_missing_deletions_do_not_change_the_index_version()
    {
        var index = new FileIndex("test");
        int parent = index.AddRoot(@"C:\test");
        var change = Change(parent);
        index.Apply([change], out bool added);
        Assert.True(added);
        long version = index.Version;

        index.Apply([change], out bool duplicate);
        Assert.False(duplicate);
        Assert.Equal(version, index.Version);

        index.Apply([new PendingChange { Parent = parent, Name = "missing.txt", FullPath = @"C:\test\missing.txt" }], out bool missing);
        Assert.False(missing);
        Assert.Equal(version, index.Version);
    }

    [Fact]
    public void Real_metadata_changes_and_deletion_advance_the_version_once()
    {
        var index = new FileIndex("test");
        int parent = index.AddRoot(@"C:\test");
        var change = Change(parent);
        index.Apply([change]);
        long version = index.Version;

        change.Size++;
        change.Modified++;
        change.Flags = EntryFlags.Hidden;
        index.Apply([change], out bool updated);
        Assert.True(updated);
        Assert.Equal(version + 1, index.Version);
        var hit = Assert.Single(SearchEngine.Run(index, new SearchRequest { Query = "file.txt", ShowHidden = true }).Items);
        Assert.Equal(change.Size, hit.Size);
        Assert.True(hit.Hidden);

        change.Exists = false;
        index.Apply([change], out bool removed);
        Assert.True(removed);
        Assert.Equal(version + 2, index.Version);
        Assert.Equal(1, index.LiveCount);
        index.Apply([change], out bool duplicate);
        Assert.False(duplicate);
        Assert.Equal(version + 2, index.Version);
    }

    static PendingChange Change(int parent) => new()
    {
        Parent = parent, Name = "file.txt", FullPath = @"C:\test\file.txt",
        Exists = true, Size = 12, Modified = DateTime.UtcNow.Ticks,
    };
}
