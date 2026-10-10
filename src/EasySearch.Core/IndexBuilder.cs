using System.Collections.Concurrent;
using System.IO.Enumeration;

namespace EasySearch.Core;

/// <summary>
/// Fills a <see cref="FileIndex"/> by walking folders in parallel. Each folder is read with a
/// single directory query that already carries sizes, dates and attributes, so no per-file
/// system calls are needed.
/// </summary>
public static class IndexBuilder
{
    static readonly EnumerationOptions Options = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    public static FileIndex Build(IndexScope scope, CancellationToken cancel = default,
        int workers = 0, Action<FileIndex>? onCreated = null, ThreadPriority priority = ThreadPriority.Normal)
    {
        var index = new FileIndex(scope.Signature);
        onCreated?.Invoke(index);

        var start = new List<(string, int)>();
        foreach (var root in scope.Roots)
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
            var worker = new Worker(index, scope);
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
                    worker.ScanFolder(folder.Path, folder.Index, stack, ref pending);
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

    sealed class Worker(FileIndex index, IndexScope scope)
    {
        readonly List<RawEntry> _entries = new(256);
        readonly List<string> _folderPaths = new(32);
        readonly List<int> _folderIndices = new(32);
        char[] _names = new char[8192];
        int _namesLength;
        string _current = "";
        FileSystemEnumerable<bool>.FindTransform? _collect;

        public void ScanFolder(string path, int parentIndex, ConcurrentStack<(string, int)> stack, ref int pending)
        {
            _entries.Clear();
            _folderPaths.Clear();
            _namesLength = 0;
            _current = path;
            _collect ??= Collect;

            foreach (var _ in new FileSystemEnumerable<bool>(PathUtil.Display(path), _collect, Options)) { }

            index.AddChildren(parentIndex, _names.AsSpan(0, _namesLength), _entries, _folderIndices);
            if (_folderIndices.Count != _folderPaths.Count) return; // parent was removed meanwhile

            int f = 0;
            foreach (ref readonly var e in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_entries))
            {
                if ((e.Flags & EntryFlags.Directory) == 0) continue;
                string folderPath = _folderPaths[f];
                int folderIndex = _folderIndices[f++];
                if ((e.Flags & EntryFlags.Reparse) != 0 && IsLink(folderPath)) continue;

                Interlocked.Increment(ref pending);
                stack.Push((folderPath, folderIndex));
            }
        }

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

        /// <summary>Junctions and symlinks are listed but not followed, to avoid loops and duplicates.</summary>
        static bool IsLink(string folderPath)
        {
            try { return new DirectoryInfo(folderPath).LinkTarget is not null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
        }
    }
}
