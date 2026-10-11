using System.Runtime.InteropServices;
using System.Text;

namespace EasySearch.Core;

[Flags]
public enum EntryFlags : byte
{
    None = 0,
    Directory = 1,
    Hidden = 2,
    System = 4,
    Reparse = 8,
    /// <summary>Lives somewhere below a hidden folder (inherited at index time).</summary>
    HiddenTree = 16,
    Deleted = 128,
}

/// <summary>One directory entry as produced by the scanner; the name lives in a shared char buffer.</summary>
public struct RawEntry
{
    public int NameStart;
    public ushort NameLength;
    public long Size;
    public long Modified;
    public EntryFlags Flags;
}

/// <summary>The on-disk state of a path that changed, with its parent already resolved to an index entry.</summary>
public sealed class PendingChange
{
    public required string FullPath;
    public required string Name;
    public required int Parent;
    public bool Exists;
    public long Size;
    public long Modified;
    public EntryFlags Flags;
}

/// <summary>What one folder contains on disk right now, to be reconciled with the index.</summary>
public sealed class FolderSnapshot
{
    /// <summary>The folder's entry in the index.</summary>
    public required int Index;
    public required string Path;
    public required char[] Names;
    public required RawEntry[] Entries;
}

/// <summary>
/// A consistent, lock-free view of the index. Entries are append-only, so everything below
/// <see cref="Count"/> stays valid even while the index keeps growing.
/// </summary>
public readonly struct IndexSnapshot
{
    public readonly char[] Names;
    public readonly int[] NameStart;
    public readonly ushort[] NameLength;
    public readonly int[] Parent;
    public readonly long[] Size;
    public readonly long[] Modified;
    public readonly EntryFlags[] Flags;
    public readonly int Count;
    public readonly int NamesLength;

    internal IndexSnapshot(char[] names, int[] nameStart, ushort[] nameLength, int[] parent,
        long[] size, long[] modified, EntryFlags[] flags, int count, int namesLength)
    {
        Names = names; NameStart = nameStart; NameLength = nameLength; Parent = parent;
        Size = size; Modified = modified; Flags = flags; Count = count; NamesLength = namesLength;
    }

    public ReadOnlySpan<char> Name(int i) => Names.AsSpan(NameStart[i], NameLength[i]);

    public bool IsDirectory(int i) => (Flags[i] & EntryFlags.Directory) != 0;

    public int PathLength(int i)
    {
        int length = -1;
        for (int p = i; p >= 0; p = Parent[p]) length += NameLength[p] + 1;
        return length;
    }

    /// <summary>Writes the full path of entry <paramref name="i"/>; <paramref name="destination"/> must hold <see cref="PathLength"/> chars.</summary>
    public int WritePath(int i, Span<char> destination)
    {
        int length = PathLength(i);
        int pos = length;
        for (int p = i; p >= 0; p = Parent[p])
        {
            int n = NameLength[p];
            pos -= n;
            Names.AsSpan(NameStart[p], n).CopyTo(destination[pos..]);
            if (Parent[p] >= 0) destination[--pos] = '\\';
        }
        return length;
    }

    public string GetPath(int i)
    {
        int length = PathLength(i);
        Span<char> buffer = length <= 512 ? stackalloc char[512] : new char[length];
        WritePath(i, buffer);
        return PathUtil.Display(new string(buffer[..length]));
    }

    public string GetDirectory(int i) => Parent[i] < 0 ? "" : GetPath(Parent[i]);
}

/// <summary>
/// In-memory file name index, stored as parallel arrays (struct-of-arrays) so a search is a
/// tight, cache-friendly scan. Paths are not stored: each entry points at its parent folder.
/// A child always has a higher index than its parent, which several passes below rely on.
/// </summary>
public sealed class FileIndex
{
    const int Magic = 0x58495345; // "ESIX"
    const int FormatVersion = 2;

    readonly object _gate = new();
    readonly Dictionary<DirKey, int> _dirs = new(DirKeyComparer.Instance);
    readonly List<(string Path, int Index)> _roots = [];

    char[] _names = new char[1 << 18];
    int _namesLength;
    int[] _nameStart = new int[1 << 14];
    ushort[] _nameLength = new ushort[1 << 14];
    int[] _parent = new int[1 << 14];
    long[] _size = new long[1 << 14];
    long[] _modified = new long[1 << 14];
    EntryFlags[] _flags = new EntryFlags[1 << 14];
    int _count;
    int _live;
    long _version;

    public FileIndex(string signature) => Signature = signature;

    /// <summary>Identifies the scope (locations + exclusions) this index was built for.</summary>
    public string Signature { get; }

    public int Count => Volatile.Read(ref _count);
    public int LiveCount => Volatile.Read(ref _live);

    /// <summary>Entries that were removed but still occupy space until the index is compacted.</summary>
    public int DeadCount => Count - LiveCount;

    /// <summary>
    /// Per volume ("C:"): the change-journal position this index is up to date with. Persisted
    /// with the cache so the next start can continue from there. Owned by <see cref="IndexService"/>.
    /// </summary>
    public Dictionary<string, (long JournalId, long Position)> Checkpoints { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per location: when it was last scanned in full (UTC ticks).</summary>
    public Dictionary<string, long> RootScanTimes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public long Version => Interlocked.Read(ref _version);

    public IndexSnapshot Snapshot()
    {
        lock (_gate)
            return new IndexSnapshot(_names, _nameStart, _nameLength, _parent, _size, _modified, _flags, _count, _namesLength);
    }

    public (string Path, int Index)[] Roots()
    {
        lock (_gate) return [.. _roots];
    }

    // ---- building ----------------------------------------------------------------------

    public int AddRoot(string normalizedPath)
    {
        long modified = 0;
        try { modified = Directory.GetLastWriteTimeUtc(PathUtil.Display(normalizedPath)).Ticks; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        lock (_gate)
        {
            int index = AppendLocked(-1, normalizedPath, 0, modified, EntryFlags.Directory);
            _roots.Add((normalizedPath, index));
            _version++;
            return index;
        }
    }

    /// <summary>Appends the contents of one folder. Fills <paramref name="directoryIndices"/> with the index of each sub-folder, in order.</summary>
    public void AddChildren(int parent, ReadOnlySpan<char> names, List<RawEntry> entries, List<int> directoryIndices)
    {
        directoryIndices.Clear();
        if (entries.Count == 0) return;

        lock (_gate)
        {
            if ((_flags[parent] & EntryFlags.Deleted) != 0) return;
            EnsureCapacity(_count + entries.Count, _namesLength + names.Length);
            long addedBytes = 0;
            foreach (ref readonly var e in CollectionsMarshal.AsSpan(entries))
            {
                int index = AppendLocked(parent, names.Slice(e.NameStart, e.NameLength), e.Size, e.Modified, e.Flags, aggregate: false);
                addedBytes += _size[index];
                if ((e.Flags & EntryFlags.Directory) != 0) directoryIndices.Add(index);
            }
            AdjustAncestorsLocked(parent, addedBytes);
            _version++;
        }
    }

    int AppendLocked(int parent, ReadOnlySpan<char> name, long size, long modified, EntryFlags flags, bool aggregate = true)
    {
        if (name.Length > ushort.MaxValue) name = name[..ushort.MaxValue];
        EnsureCapacity(_count + 1, _namesLength + name.Length);

        int i = _count;
        name.CopyTo(_names.AsSpan(_namesLength));
        _nameStart[i] = _namesLength;
        _nameLength[i] = (ushort)name.Length;
        _namesLength += name.Length;
        _parent[i] = parent;
        // Directories contain the sum of indexed descendant files, never a scanner's
        // directory-entry size. Updating ancestors avoids any filesystem work at search time.
        _size[i] = (flags & EntryFlags.Directory) != 0 ? 0 : size;
        if (aggregate) AdjustAncestorsLocked(parent, _size[i]);
        _modified[i] = modified;
        if (parent >= 0 && (_flags[parent] & (EntryFlags.Hidden | EntryFlags.HiddenTree)) != 0)
            flags |= EntryFlags.HiddenTree;
        _flags[i] = flags;
        if (parent >= 0 && (flags & EntryFlags.Directory) != 0)
            _dirs[new DirKey(parent, name.ToString())] = i;

        _count = i + 1;
        _live++;
        return i;
    }

    void EnsureCapacity(int entries, int chars)
    {
        if (chars > _names.Length)
            Array.Resize(ref _names, Math.Max(chars, _names.Length * 2));
        if (entries <= _nameStart.Length) return;

        int capacity = Math.Max(entries, _nameStart.Length * 2);
        Array.Resize(ref _nameStart, capacity);
        Array.Resize(ref _nameLength, capacity);
        Array.Resize(ref _parent, capacity);
        Array.Resize(ref _size, capacity);
        Array.Resize(ref _modified, capacity);
        Array.Resize(ref _flags, capacity);
    }

    /// <summary>Releases the spare capacity left over from growing while scanning.</summary>
    public void TrimExcess()
    {
        lock (_gate)
        {
            int capacity = _count + Math.Max(1024, _count / 64);
            if (capacity >= _nameStart.Length) return;
            Array.Resize(ref _names, _namesLength + Math.Max(4096, _namesLength / 64));
            Array.Resize(ref _nameStart, capacity);
            Array.Resize(ref _nameLength, capacity);
            Array.Resize(ref _parent, capacity);
            Array.Resize(ref _size, capacity);
            Array.Resize(ref _modified, capacity);
            Array.Resize(ref _flags, capacity);
            _dirs.TrimExcess();
        }
    }

    // ---- path lookups ------------------------------------------------------------------

    /// <summary>Index of the folder at <paramref name="normalizedPath"/>, or -1 when it is not indexed.</summary>
    public int Resolve(string normalizedPath)
    {
        lock (_gate) return ResolveLocked(normalizedPath);
    }

    int ResolveLocked(ReadOnlySpan<char> path)
    {
        int current = -1, rootLength = -1;
        foreach (var (rootPath, index) in _roots)
        {
            if (rootPath.Length > rootLength && PathUtil.IsSameOrUnder(path, rootPath))
            {
                current = index;
                rootLength = rootPath.Length;
            }
        }
        if (current < 0) return -1;

        var rest = path.Length > rootLength ? path[(rootLength + 1)..] : default;
        while (rest.Length > 0)
        {
            int cut = rest.IndexOf('\\');
            var part = cut < 0 ? rest : rest[..cut];
            rest = cut < 0 ? default : rest[(cut + 1)..];
            if (part.Length == 0) continue;
            if (!_dirs.TryGetValue(new DirKey(current, part.ToString()), out current)) return -1;
        }
        return current;
    }

    /// <summary>
    /// Marks everything strictly below <paramref name="scopePath"/>. The folder may be an indexed
    /// folder, or an ancestor of one or more index locations.
    /// </summary>
    public bool[] BuildScopeMask(string scopePath, in IndexSnapshot snapshot)
    {
        var mask = new bool[snapshot.Count];
        string scope;
        try { scope = PathUtil.Normalize(scopePath); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { return mask; }

        int target;
        var rootsBelow = new HashSet<int>();
        lock (_gate)
        {
            target = ResolveLocked(scope);
            foreach (var (rootPath, index) in _roots)
                if (index != target && PathUtil.IsSameOrUnder(rootPath, scope)) rootsBelow.Add(index);
        }

        for (int i = 0; i < mask.Length; i++)
        {
            int p = snapshot.Parent[i];
            mask[i] = p < 0 ? rootsBelow.Contains(i) : p == target || mask[p];
        }
        return mask;
    }

    // ---- live updates ------------------------------------------------------------------

    /// <summary>
    /// Reconciles the index with what is now on disk for a batch of changed paths.
    /// Returns folders that are new to the index and still need their contents scanned.
    /// </summary>
    public List<(string Path, int Index)> Apply(IReadOnlyList<PendingChange> changes)
        => Apply(changes, out _);

    public List<(string Path, int Index)> Apply(IReadOnlyList<PendingChange> changes, out bool changed)
    {
        var newDirectories = new List<(string, int)>();
        changed = false;
        if (changes.Count == 0) return newDirectories;

        lock (_gate)
        {
            // Find the existing entry (if any) for each change in a single pass over the index.
            var wanted = new Dictionary<int, Dictionary<string, int>>();
            var touchedParents = new bool[_count];
            for (int k = 0; k < changes.Count; k++)
            {
                var c = changes[k];
                if ((uint)c.Parent >= (uint)_count) continue;
                if (!wanted.TryGetValue(c.Parent, out var byName))
                    wanted[c.Parent] = byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                byName[c.Name] = k;
                touchedParents[c.Parent] = true;
            }

            var found = new int[changes.Count];
            Array.Fill(found, -1);
            for (int i = 0; i < _count; i++)
            {
                int p = _parent[i];
                if (p < 0 || !touchedParents[p] || (_flags[i] & EntryFlags.Deleted) != 0) continue;
                var lookup = wanted[p].GetAlternateLookup<ReadOnlySpan<char>>();
                if (lookup.TryGetValue(_names.AsSpan(_nameStart[i], _nameLength[i]), out int k)) found[k] = i;
            }

            bool sweep = false;
            foreach (var byName in wanted.Values)
            {
                foreach (int k in byName.Values)
                {
                    var c = changes[k];
                    int i = found[k];
                    if ((_flags[c.Parent] & EntryFlags.Deleted) != 0) continue;

                    bool isDirectory = (c.Flags & EntryFlags.Directory) != 0;
                    if (i >= 0)
                    {
                        bool wasDirectory = (_flags[i] & EntryFlags.Directory) != 0;
                        if (c.Exists && wasDirectory == isDirectory)
                        {
                            var flags = c.Flags | (_flags[i] & EntryFlags.HiddenTree);
                            if ((!isDirectory && _size[i] != c.Size) || _modified[i] != c.Modified || _flags[i] != flags)
                            {
                                if (!isDirectory) SetFileSizeLocked(i, c.Size);
                                _modified[i] = c.Modified;
                                _flags[i] = flags;
                                changed = true;
                            }
                            continue;
                        }
                        KillLocked(i);
                        changed = true;
                        sweep |= wasDirectory;
                    }
                    if (!c.Exists) continue;

                    int index = AppendLocked(c.Parent, c.Name, c.Size, c.Modified, c.Flags);
                    changed = true;
                    if (isDirectory) newDirectories.Add((c.FullPath, index));
                }
            }

            if (sweep) SweepLocked();
            if (changed) _version++;
        }
        return newDirectories;
    }

    /// <summary>
    /// Makes the index match what the given folders contain on disk: adds what is new, updates
    /// sizes and dates, and removes what is gone. Returns folders that are new to the index and
    /// still need their contents scanned.
    /// </summary>
    public List<(string Path, int Index)> SyncFolders(IReadOnlyList<FolderSnapshot> folders, out bool changed)
    {
        var newDirectories = new List<(string, int)>();
        changed = false;
        if (folders.Count == 0) return newDirectories;

        lock (_gate)
        {
            // Collect what the index currently holds in each of these folders, in one pass.
            var known = new Dictionary<int, Dictionary<string, int>>();
            var touched = new bool[_count];
            foreach (var folder in folders)
            {
                if ((uint)folder.Index >= (uint)_count || known.ContainsKey(folder.Index)) continue;
                known[folder.Index] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                touched[folder.Index] = true;
            }

            bool sweep = false;
            for (int i = 0; i < _count; i++)
            {
                int p = _parent[i];
                if (p < 0 || !touched[p] || (_flags[i] & EntryFlags.Deleted) != 0) continue;
                var name = new string(_names, _nameStart[i], _nameLength[i]);
                if (known[p].TryGetValue(name, out int duplicate))
                {
                    sweep |= (_flags[duplicate] & EntryFlags.Directory) != 0;
                    KillLocked(duplicate);
                    changed = true;
                }
                known[p][name] = i;
            }

            foreach (var folder in folders)
            {
                if (!known.Remove(folder.Index, out var existing)) continue; // listed twice, or out of range
                var folderFlags = _flags[folder.Index];
                if ((folderFlags & EntryFlags.Deleted) != 0 || (folderFlags & EntryFlags.Directory) == 0) continue;

                var lookup = existing.GetAlternateLookup<ReadOnlySpan<char>>();
                foreach (ref readonly var e in folder.Entries.AsSpan())
                {
                    var name = folder.Names.AsSpan(e.NameStart, e.NameLength);
                    bool isDirectory = (e.Flags & EntryFlags.Directory) != 0;
                    if (lookup.Remove(name, out _, out int i))
                    {
                        bool wasDirectory = (_flags[i] & EntryFlags.Directory) != 0;
                        if (wasDirectory == isDirectory)
                        {
                            var flags = e.Flags | (_flags[i] & EntryFlags.HiddenTree);
                            if ((!isDirectory && _size[i] != e.Size) || _modified[i] != e.Modified || _flags[i] != flags)
                            {
                                if (!isDirectory) SetFileSizeLocked(i, e.Size);
                                _modified[i] = e.Modified;
                                _flags[i] = flags;
                                changed = true;
                            }
                            continue;
                        }
                        KillLocked(i);
                        sweep |= wasDirectory;
                    }

                    int index = AppendLocked(folder.Index, name, e.Size, e.Modified, e.Flags);
                    changed = true;
                    if (isDirectory) newDirectories.Add((string.Concat(folder.Path, "\\", name), index));
                }

                foreach (int gone in existing.Values)
                {
                    sweep |= (_flags[gone] & EntryFlags.Directory) != 0;
                    KillLocked(gone);
                    changed = true;
                }
            }

            if (sweep) SweepLocked();
            if (changed) _version++;
        }
        return newDirectories;
    }

    /// <summary>
    /// Swaps the contents of one location for a freshly scanned copy (an index built with
    /// <see cref="IndexBuilder.BuildRoot"/>). Everything else in the index is left alone.
    /// </summary>
    public void ReplaceRoot(string rootPath, FileIndex fresh)
    {
        var source = fresh.Snapshot();
        if (source.Count == 0) return; // location was unavailable: keep what we have

        lock (_gate)
        {
            int rootIndex = -1;
            foreach (var (path, index) in _roots)
                if (string.Equals(path, rootPath, StringComparison.OrdinalIgnoreCase)) rootIndex = index;
            if (rootIndex < 0)
            {
                rootIndex = AppendLocked(-1, rootPath, 0, source.Modified[0], EntryFlags.Directory);
                _roots.Add((rootPath, rootIndex));
            }

            // Drop the old contents: everything whose chain of parents leads to this root.
            var below = new bool[_count];
            for (int i = rootIndex + 1; i < below.Length; i++)
            {
                int p = _parent[i];
                if (p < 0 || !(p == rootIndex || below[p])) continue;
                below[i] = true;
                if ((_flags[i] & EntryFlags.Deleted) == 0) KillLocked(i);
            }

            // Append the new contents. Parents come before children, so their new index is already known.
            EnsureCapacity(_count + source.Count, _namesLength + source.NamesLength);
            var moved = new int[source.Count];
            moved[0] = rootIndex;
            for (int i = 1; i < source.Count; i++)
            {
                int p = source.Parent[i] < 0 ? -1 : moved[source.Parent[i]];
                moved[i] = p < 0 || (source.Flags[i] & EntryFlags.Deleted) != 0
                    ? -1
                    : AppendLocked(p, source.Name(i), source.Size[i], source.Modified[i], source.Flags[i] & ~EntryFlags.HiddenTree);
            }
            _modified[rootIndex] = source.Modified[0];
            _version++;
        }
    }

    /// <summary>A copy of this index without the removed entries.</summary>
    public FileIndex Compact()
    {
        lock (_gate)
        {
            var compact = new FileIndex(Signature);
            compact.EnsureCapacity(_live + 1024, _namesLength);

            var moved = new int[_count];
            for (int i = 0; i < _count; i++)
            {
                int p = _parent[i] < 0 ? -1 : moved[_parent[i]];
                bool keep = (_flags[i] & EntryFlags.Deleted) == 0 && (_parent[i] < 0 || p >= 0);
                moved[i] = keep
                    ? compact.AppendLocked(p, _names.AsSpan(_nameStart[i], _nameLength[i]), _size[i], _modified[i], _flags[i] & ~EntryFlags.HiddenTree)
                    : -1;
            }

            foreach (var (path, index) in _roots)
                if (moved[index] >= 0) compact._roots.Add((path, moved[index]));
            foreach (var (volume, checkpoint) in Checkpoints) compact.Checkpoints[volume] = checkpoint;
            foreach (var (root, time) in RootScanTimes) compact.RootScanTimes[root] = time;
            return compact;
        }
    }

    /// <summary>Children come after their parent, so one forward pass removes whole subtrees of removed folders.</summary>
    void SweepLocked()
    {
        for (int i = 0; i < _count; i++)
        {
            int p = _parent[i];
            if (p >= 0 && (_flags[i] & EntryFlags.Deleted) == 0 && (_flags[p] & EntryFlags.Deleted) != 0)
                KillLocked(i);
        }
    }

    void KillLocked(int i)
    {
        AdjustAncestorsLocked(_parent[i], -_size[i]);
        _size[i] = 0;
        if ((_flags[i] & EntryFlags.Directory) != 0)
            _dirs.Remove(new DirKey(_parent[i], new string(_names, _nameStart[i], _nameLength[i])));
        _flags[i] |= EntryFlags.Deleted;
        _live--;
    }

    void SetFileSizeLocked(int i, long size)
    {
        AdjustAncestorsLocked(_parent[i], size - _size[i]);
        _size[i] = size;
    }

    void AdjustAncestorsLocked(int parent, long delta)
    {
        if (delta == 0) return;
        for (int p = parent; p >= 0 && (_flags[p] & EntryFlags.Deleted) == 0; p = _parent[p])
            _size[p] += delta;
    }

    // ---- persistence -------------------------------------------------------------------

    public void Save(string file)
    {
        var s = Snapshot();
        var roots = Roots();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        var temp = file + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(Signature);
            writer.Write(s.Count);
            writer.Write(s.NamesLength);
            writer.Write(roots.Length);
            foreach (var (path, index) in roots)
            {
                writer.Write(path);
                writer.Write(index);
            }
            writer.Write(Checkpoints.Count);
            foreach (var (volume, checkpoint) in Checkpoints)
            {
                writer.Write(volume);
                writer.Write(checkpoint.JournalId);
                writer.Write(checkpoint.Position);
            }
            writer.Write(RootScanTimes.Count);
            foreach (var (root, time) in RootScanTimes)
            {
                writer.Write(root);
                writer.Write(time);
            }
            writer.Write(MemoryMarshal.AsBytes(s.Names.AsSpan(0, s.NamesLength)));
            writer.Write(MemoryMarshal.AsBytes(s.NameStart.AsSpan(0, s.Count)));
            writer.Write(MemoryMarshal.AsBytes(s.NameLength.AsSpan(0, s.Count)));
            writer.Write(MemoryMarshal.AsBytes(s.Parent.AsSpan(0, s.Count)));
            writer.Write(MemoryMarshal.AsBytes(s.Size.AsSpan(0, s.Count)));
            writer.Write(MemoryMarshal.AsBytes(s.Modified.AsSpan(0, s.Count)));
            writer.Write(MemoryMarshal.AsBytes(s.Flags.AsSpan(0, s.Count)));
        }
        File.Move(temp, file, overwrite: true);
    }

    /// <summary>Loads a cached index, or returns null when it is missing, unreadable or built for another scope.</summary>
    public static FileIndex? TryLoad(string file, string signature)
    {
        try
        {
            if (!File.Exists(file)) return null;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            if (reader.ReadInt32() != Magic || reader.ReadInt32() != FormatVersion) return null;
            if (reader.ReadString() != signature) return null;
            int count = reader.ReadInt32();
            int namesLength = reader.ReadInt32();
            int rootCount = reader.ReadInt32();
            if (count < 0 || namesLength < 0 || rootCount < 0) return null;

            var index = new FileIndex(signature);
            for (int r = 0; r < rootCount; r++)
                index._roots.Add((reader.ReadString(), reader.ReadInt32()));
            for (int c = reader.ReadInt32(); c > 0; c--)
                index.Checkpoints[reader.ReadString()] = (reader.ReadInt64(), reader.ReadInt64());
            for (int s = reader.ReadInt32(); s > 0; s--)
                index.RootScanTimes[reader.ReadString()] = reader.ReadInt64();

            index._names = new char[Math.Max(namesLength, 16)];
            index._nameStart = new int[Math.Max(count, 16)];
            index._nameLength = new ushort[index._nameStart.Length];
            index._parent = new int[index._nameStart.Length];
            index._size = new long[index._nameStart.Length];
            index._modified = new long[index._nameStart.Length];
            index._flags = new EntryFlags[index._nameStart.Length];

            stream.ReadExactly(MemoryMarshal.AsBytes(index._names.AsSpan(0, namesLength)));
            stream.ReadExactly(MemoryMarshal.AsBytes(index._nameStart.AsSpan(0, count)));
            stream.ReadExactly(MemoryMarshal.AsBytes(index._nameLength.AsSpan(0, count)));
            stream.ReadExactly(MemoryMarshal.AsBytes(index._parent.AsSpan(0, count)));
            stream.ReadExactly(MemoryMarshal.AsBytes(index._size.AsSpan(0, count)));
            stream.ReadExactly(MemoryMarshal.AsBytes(index._modified.AsSpan(0, count)));
            stream.ReadExactly(MemoryMarshal.AsBytes(index._flags.AsSpan(0, count)));
            index._namesLength = namesLength;
            index._count = count;

            for (int i = 0; i < count; i++)
            {
                int p = index._parent[i];
                long end = (long)index._nameStart[i] + index._nameLength[i];
                if (p >= i || p < -1 || index._nameStart[i] < 0 || end > namesLength) return null;

                var flags = index._flags[i];
                if ((flags & EntryFlags.Deleted) != 0) continue;
                index._live++;
                if (p >= 0 && (flags & EntryFlags.Directory) != 0)
                    index._dirs[new DirKey(p, new string(index._names, index._nameStart[i], index._nameLength[i]))] = i;
            }
            // Upgrade older v2 caches (which stored zero directory sizes) using only
            // indexed metadata. Reverse order works because parents precede children.
            for (int i = 0; i < count; i++)
                if ((index._flags[i] & EntryFlags.Directory) != 0) index._size[i] = 0;
            for (int i = count - 1; i >= 0; i--)
            {
                int p = index._parent[i];
                if (p >= 0 && (index._flags[i] & EntryFlags.Deleted) == 0 && (index._flags[p] & EntryFlags.Deleted) == 0)
                    index._size[p] += index._size[i];
            }
            return index;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return null;
        }
    }

    readonly record struct DirKey(int Parent, string Name);

    sealed class DirKeyComparer : IEqualityComparer<DirKey>
    {
        public static readonly DirKeyComparer Instance = new();

        public bool Equals(DirKey x, DirKey y) =>
            x.Parent == y.Parent && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(DirKey key) =>
            HashCode.Combine(key.Parent, StringComparer.OrdinalIgnoreCase.GetHashCode(key.Name));
    }
}
