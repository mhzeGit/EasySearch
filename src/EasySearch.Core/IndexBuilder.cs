using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Runtime.InteropServices;

namespace EasySearch.Core;

/// <summary>
/// Lists one folder with a single directory query that already carries sizes, dates and
/// attributes, so no per-file system calls are needed. Reused across folders to avoid garbage.
/// </summary>
public sealed class FolderReader(IndexScope scope)
{
    static readonly EnumerationOptions Options = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    readonly List<RawEntry> _entries = new(256);
    readonly List<string> _folderPaths = new(32);
    char[] _names = new char[8192];
    int _namesLength;
    string _current = "";
    FileSystemEnumerable<bool>.FindTransform? _collect;

    /// <summary>Everything in the folder that belongs in the index (excluded folders are left out).</summary>
    public List<RawEntry> Entries => _entries;

    /// <summary>The names of <see cref="Entries"/>, back to back.</summary>
    public ReadOnlySpan<char> Names => _names.AsSpan(0, _namesLength);

    /// <summary>Full path of each sub-folder in <see cref="Entries"/>, in the same order.</summary>
    public List<string> FolderPaths => _folderPaths;

    /// <summary>Reads the folder at <paramref name="path"/> (a normalized path). Throws when it cannot be opened.</summary>
    public void Read(string path)
    {
        _entries.Clear();
        _folderPaths.Clear();
        _namesLength = 0;
        _current = path;
        _collect ??= Collect;
        foreach (var _ in new FileSystemEnumerable<bool>(PathUtil.Display(path), _collect, Options)) { }
    }

    public FolderSnapshot ToSnapshot(int index) => new()
    {
        Index = index,
        Path = _current,
        Names = Names.ToArray(),
        Entries = [.. _entries],
    };

    bool Collect(ref FileSystemEntry entry)
    {
        var name = entry.FileName;
        var attributes = entry.Attributes;
        var flags = EntryFlags.None;
        if ((attributes & FileAttributes.Hidden) != 0) flags |= EntryFlags.Hidden;
        if ((attributes & FileAttributes.System) != 0) flags |= EntryFlags.System;
        if ((attributes & FileAttributes.ReparsePoint) != 0) flags |= EntryFlags.Reparse;

        if (entry.IsDirectory)
        {
            if (scope.IsExcludedName(name)) return false;
            string folderPath = string.Concat(_current, "\\", name);
            if (scope.IsExcludedPath(folderPath)) return false;
            flags |= EntryFlags.Directory;
            _folderPaths.Add(folderPath);
        }

        if (_namesLength + name.Length > _names.Length)
            Array.Resize(ref _names, Math.Max(_names.Length * 2, _namesLength + name.Length));
        name.CopyTo(_names.AsSpan(_namesLength));

        _entries.Add(new RawEntry
        {
            NameStart = _namesLength,
            NameLength = (ushort)Math.Min(name.Length, ushort.MaxValue),
            Size = entry.IsDirectory ? 0 : entry.Length,
            Modified = entry.LastWriteTimeUtc.UtcTicks,
            Flags = flags,
        });
        _namesLength += name.Length;
        return true;
    }
}

/// <summary>Fills a <see cref="FileIndex"/> by walking folders in parallel.</summary>
public static class IndexBuilder
{
    /// <summary>Builds a fresh index of every location in <paramref name="scope"/>.</summary>
    public static FileIndex Build(IndexScope scope, CancellationToken cancel = default,
        int workers = 0, Action<FileIndex>? onCreated = null, ThreadPriority priority = ThreadPriority.Normal) =>
        Build(scope, scope.Roots, cancel, workers, onCreated, priority);

    /// <summary>Builds an index holding just one of the scope's locations (used to refresh it in place).</summary>
    public static FileIndex BuildRoot(IndexScope scope, string root, CancellationToken cancel = default,
        int workers = 0, ThreadPriority priority = ThreadPriority.Normal) =>
        Build(scope, [root], cancel, workers, null, priority);

    static FileIndex Build(IndexScope scope, IEnumerable<string> roots, CancellationToken cancel,
        int workers, Action<FileIndex>? onCreated, ThreadPriority priority)
    {
        var index = new FileIndex(scope.Signature);
        onCreated?.Invoke(index);

        var start = new List<(string, int)>();
        foreach (var root in roots)
        {
            if (Directory.Exists(PathUtil.Display(root)))
                start.Add((root, index.AddRoot(root)));
        }
        Scan(index, start, scope, cancel, workers, priority);
        index.TrimExcess();
        return index;
    }

    /// <summary>Scans the given folders (already present in the index) and everything below them.</summary>
    public static void Scan(FileIndex index, IReadOnlyList<(string Path, int Index)> start, IndexScope scope,
        CancellationToken cancel = default, int workers = 0, ThreadPriority priority = ThreadPriority.Normal)
    {
        if (start.Count == 0) return;
        if (workers <= 0) workers = Math.Clamp(Environment.ProcessorCount, 2, 8);

        var stack = new ConcurrentStack<(string Path, int Index)>(start);
        int pending = start.Count;

        void Work()
        {
            var reader = new FolderReader(scope);
            var folderIndices = new List<int>(32);
            var spin = new SpinWait();
            while (!cancel.IsCancellationRequested)
            {
                if (!stack.TryPop(out var folder))
                {
                    if (Volatile.Read(ref pending) == 0) return;
                    spin.SpinOnce();
                    continue;
                }
                spin.Reset();
                try
                {
                    reader.Read(folder.Path);
                    index.AddChildren(folder.Index, reader.Names, reader.Entries, folderIndices);
                    if (folderIndices.Count != reader.FolderPaths.Count) continue; // parent was removed meanwhile

                    int f = 0;
                    foreach (ref readonly var e in CollectionsMarshal.AsSpan(reader.Entries))
                    {
                        if ((e.Flags & EntryFlags.Directory) == 0) continue;
                        string folderPath = reader.FolderPaths[f];
                        int folderIndex = folderIndices[f++];
                        if ((e.Flags & EntryFlags.Reparse) != 0 && IsLink(folderPath)) continue;

                        Interlocked.Increment(ref pending);
                        stack.Push((folderPath, folderIndex));
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // Folder vanished or cannot be read: skip it.
                }
                finally
                {
                    Interlocked.Decrement(ref pending);
                }
            }
        }

        if (workers == 1)
        {
            Work();
            return;
        }

        var threads = new Thread[workers];
        for (int t = 0; t < threads.Length; t++)
        {
            threads[t] = new Thread(Work) { IsBackground = true, Name = "EasySearch indexer", Priority = priority };
            threads[t].Start();
        }
        foreach (var thread in threads) thread.Join();
    }

    /// <summary>Scans folders that just appeared in the index. Links are listed but never followed.</summary>
    public static void ScanNew(FileIndex index, List<(string Path, int Index)> folders, IndexScope scope, CancellationToken cancel = default)
    {
        folders.RemoveAll(folder => IsLink(folder.Path));
        Scan(index, folders, scope, cancel, workers: 1);
    }

    /// <summary>Junctions and symlinks are not followed, to avoid loops and duplicates.</summary>
    static bool IsLink(string folderPath)
    {
        try { return new DirectoryInfo(folderPath).LinkTarget is not null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
    }
}
