using System.Diagnostics;

namespace EasySearch.Core;

public sealed record IndexStatus(bool Indexing, int Items, int Scanned, long Version, DateTime? LastIndexedUtc, double LastDurationMs);

/// <summary>
/// Owns the live index and keeps it true to the disk with as little work as possible:
///
/// * The drive is scanned in full once. The result is cached on disk.
/// * From then on only changes are applied. On NTFS drives they come from the change journal
///   (<see cref="UsnJournal"/>): every couple of seconds the service asks "which folders changed
///   since position N" and re-lists just those. The position is saved with the cache, so after a
///   restart the index catches up from where it stopped instead of scanning again.
/// * Drives without a journal (FAT, exFAT, network) are followed with file system notifications
///   while the app runs, and re-scanned at most once a day to catch what happened while it didn't.
/// * A location is only re-scanned when there is no other way to know what changed: the journal
///   was reset or no longer reaches back far enough, or notifications were lost.
/// </summary>
public sealed class IndexService : IDisposable
{
    const int MaxQueuedChanges = 50_000;
    const int SyncBatch = 512;
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    static readonly TimeSpan RelaxedPollInterval = TimeSpan.FromSeconds(15);
    static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(1);
    static readonly TimeSpan UnwatchedRescanAge = TimeSpan.FromHours(24);

    readonly string _cacheFile;
    readonly bool _useJournal;
    readonly object _sync = new();      // start/stop bookkeeping
    readonly object _jobLock = new();   // one scan job at a time
    readonly object _work = new();      // everything that changes the index, its journal positions, or the change sources
    readonly Dictionary<string, UsnJournal> _journals = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, long> _positions = new(StringComparer.OrdinalIgnoreCase);
    readonly List<FileSystemWatcher> _watchers = [];
    readonly HashSet<string> _touched = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _lostRoots = new(StringComparer.OrdinalIgnoreCase);
    readonly Timer _pollTimer;

    volatile FileIndex _index = new("");
    volatile FileIndex? _building;
    volatile IndexScope _scope = new([], []);
    CancellationTokenSource _cancel = new();
    HashSet<string>? _replay;           // notifications seen while a location is being re-scanned
    volatile bool _indexing;
    volatile bool _fullBuild;
    bool _dirty;
    DateTime _lastSave = DateTime.UtcNow;
    DateTime? _lastIndexedUtc;
    double _lastDurationMs;
    long _changeCounter;
    int _fullScans, _locationScans;
    bool _disposed;

    /// <param name="useJournal">False forces file system notifications everywhere (for tests and troubleshooting).</param>
    public IndexService(string cacheFile, bool useJournal = true)
    {
        _cacheFile = cacheFile;
        _useJournal = useJournal;
        _pollTimer = new Timer(_ => Poll(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Raised (on a background thread) whenever the status or the index contents change.</summary>
    public event Action? Changed;

    public FileIndex Index => _index;

    /// <summary>How many times everything was scanned from scratch since this service started.</summary>
    public int FullScans => Volatile.Read(ref _fullScans);

    /// <summary>How many single locations had to be re-scanned since this service started.</summary>
    public int LocationScans => Volatile.Read(ref _locationScans);

    public IndexStatus Status
    {
        get
        {
            var index = _index;
            return new IndexStatus(_indexing, index.LiveCount, _building?.Count ?? 0,
                Interlocked.Read(ref _changeCounter), _lastIndexedUtc, _lastDurationMs);
        }
    }

    /// <summary>Loads the cached index and brings it up to date; scans from scratch only when there is no usable cache.</summary>
    public void Start(IndexScope scope) => Launch(scope, rebuild: false);

    /// <summary>Scans everything again. The current index stays searchable until the new one is ready.</summary>
    public void Rebuild(IndexScope scope) => Launch(scope, rebuild: true);

    /// <summary>
    /// While nobody is looking at results, changes are folded in less often to save work.
    /// Call <see cref="Refresh"/> when that ends; it catches up at once.
    /// </summary>
    public bool Relaxed { get; set; }

    /// <summary>Applies pending changes now rather than at the next tick (for example when the window is shown).</summary>
    public void Refresh()
    {
        if (!_fullBuild) SchedulePoll(TimeSpan.Zero);
    }

    void Launch(IndexScope scope, bool rebuild)
    {
        CancellationToken cancel;
        lock (_sync)
        {
            if (_disposed) return;
            _cancel.Cancel();
            _cancel = new CancellationTokenSource();
            cancel = _cancel.Token;
            _scope = scope;
            _indexing = rebuild;
        }
        Changed?.Invoke();
        RunJob(() => Prepare(scope, rebuild, cancel), cancel);
    }

    void RunJob(Action job, CancellationToken cancel)
    {
        var thread = new Thread(() =>
        {
            lock (_jobLock)
            {
                try
                {
                    if (!cancel.IsCancellationRequested) job();
                }
                finally
                {
                    _building = null;
                    _fullBuild = false;
                    if (!cancel.IsCancellationRequested)
                    {
                        _indexing = false;
                        SchedulePoll();
                    }
                    Changed?.Invoke();
                }
            }
        })
        { IsBackground = true, Name = "EasySearch index job" };
        thread.Start();
    }

    // ---- start-up ----------------------------------------------------------------------

    void Prepare(IndexScope scope, bool rebuild, CancellationToken cancel)
    {
        var cached = rebuild ? null : FileIndex.TryLoad(_cacheFile, scope.Signature);

        List<string> stale;
        lock (_work)
        {
            OpenChangeSources(scope);
            if (cached is null)
            {
                stale = [];
            }
            else
            {
                _index = cached;
                try { _lastIndexedUtc = File.GetLastWriteTimeUtc(_cacheFile); } catch (IOException) { }

                // Resume each journal where the cached index left off, if it still reaches back that far.
                foreach (var (volume, journal) in _journals)
                {
                    if (cached.Checkpoints.TryGetValue(volume, out var checkpoint)
                        && checkpoint.JournalId == journal.JournalId
                        && journal.TryQuery(out long first, out long next)
                        && checkpoint.Position >= first && checkpoint.Position <= next)
                    {
                        _positions[volume] = checkpoint.Position;
                    }
                }
                stale = StaleRoots(cached, scope);
            }
        }

        if (cached is null)
        {
            BuildEverything(scope, cancel);
            return;
        }

        Interlocked.Increment(ref _changeCounter);
        _indexing = stale.Count > 0;
        Changed?.Invoke();
        Poll(); // catch up on what changed while the app was closed

        foreach (var root in stale)
        {
            if (cancel.IsCancellationRequested) return;
            RescanRoot(scope, root, cancel);
        }
        // Persist the catch-up before startup completes, rather than relying on a clean exit.
        lock (_work)
        {
            if (_dirty && !cancel.IsCancellationRequested) Save();
        }
    }

    /// <summary>Locations whose changes since the cache was written cannot be known without looking.</summary>
    List<string> StaleRoots(FileIndex cached, IndexScope scope)
    {
        var known = cached.Roots().Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stale = new List<string>();
        foreach (var root in scope.Roots)
        {
            bool isStale;
            if (!known.Contains(root)) isStale = Directory.Exists(PathUtil.Display(root)); // was unavailable last time
            else if (VolumeOf(root) is { } volume && _journals.ContainsKey(volume)) isStale = !_positions.ContainsKey(volume);
            else isStale = DateTime.UtcNow.Ticks - cached.RootScanTimes.GetValueOrDefault(root) > UnwatchedRescanAge.Ticks;
            if (isStale) stale.Add(root);
        }
        return stale;
    }

    void BuildEverything(IndexScope scope, CancellationToken cancel)
    {
        // Note where each journal stands before scanning: everything after that point is replayed
        // once the scan is done, so nothing that changes during the scan is missed.
        var starts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        lock (_work)
        {
            foreach (var (volume, journal) in _journals)
                if (journal.TryQuery(out _, out long next)) starts[volume] = next;
        }

        _fullBuild = true;
        _indexing = true;
        Changed?.Invoke();
        var timer = Stopwatch.StartNew();
        var built = IndexBuilder.Build(scope, cancel, onCreated: index =>
        {
            _building = index;
            // Nothing useful to show yet (first run or new scope): search the index as it fills up.
            if (_index.Count == 0 || _index.Signature != index.Signature) _index = index;
        });
        if (cancel.IsCancellationRequested) return;

        lock (_work)
        {
            _index = built;
            _positions.Clear();
            foreach (var (volume, position) in starts) _positions[volume] = position;
            foreach (var root in scope.Roots) built.RootScanTimes[root] = DateTime.UtcNow.Ticks;
            lock (_touched) _touched.Clear();
            _fullBuild = false;
            Save();
        }
        Interlocked.Increment(ref _fullScans);
        Interlocked.Increment(ref _changeCounter);
        _lastIndexedUtc = DateTime.UtcNow;
        _lastDurationMs = timer.Elapsed.TotalMilliseconds;
    }

    /// <summary>Scans one location again and swaps the result in, leaving the rest of the index untouched.</summary>
    void RescanRoot(IndexScope scope, string root, CancellationToken cancel)
    {
        string? volume = VolumeOf(root);
        long? start = null;
        lock (_work)
        {
            if (volume is not null && _journals.TryGetValue(volume, out var journal) && journal.TryQuery(out _, out long next)) start = next;
            lock (_touched) _replay ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var timer = Stopwatch.StartNew();
        var fresh = IndexBuilder.BuildRoot(scope, root, cancel,
            workers: Math.Clamp(Environment.ProcessorCount / 2, 2, 4), priority: ThreadPriority.BelowNormal);
        if (cancel.IsCancellationRequested) return;

        lock (_work)
        {
            var index = _index;
            index.ReplaceRoot(root, fresh);
            index.RootScanTimes[root] = DateTime.UtcNow.Ticks;

            // Re-read the journal from before the scan started; notifications seen meanwhile are replayed too.
            if (start is { } position)
                _positions[volume!] = Math.Min(_positions.GetValueOrDefault(volume!, position), position);
            lock (_touched)
            {
                if (_replay is not null) _touched.UnionWith(_replay);
                _replay = null;
            }

            if (index.DeadCount > index.Count / 4) _index = index.Compact();
            _dirty = true;
            Save(); // a completed recovery scan must survive an immediate restart
        }
        Interlocked.Increment(ref _locationScans);
        Interlocked.Increment(ref _changeCounter);
        _lastIndexedUtc = DateTime.UtcNow;
        _lastDurationMs = timer.Elapsed.TotalMilliseconds;
        Changed?.Invoke();
    }

    // ---- change sources ----------------------------------------------------------------

    static string? VolumeOf(string normalizedRoot) =>
        normalizedRoot.Length >= 2 && normalizedRoot[1] == ':' ? normalizedRoot[..2] : null;

    /// <summary>Opens the change journal of each volume that has one, and watches the remaining locations.</summary>
    void OpenChangeSources(IndexScope scope)
    {
        CloseChangeSources();
        foreach (var root in scope.Roots)
        {
            string? volume = VolumeOf(root);
            if (_useJournal && volume is not null)
            {
                if (_journals.ContainsKey(volume)) continue;
                if (UsnJournal.TryOpen(volume) is { } journal)
                {
                    _journals[volume] = journal;
                    continue;
                }
            }
            Watch(root);
        }
    }

    void CloseChangeSources()
    {
        foreach (var journal in _journals.Values) journal.Dispose();
        _journals.Clear();
        _positions.Clear();
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
        lock (_touched)
        {
            _touched.Clear();
            _lostRoots.Clear();
        }
    }

    void Watch(string root)
    {
        try
        {
            var watcher = new FileSystemWatcher(PathUtil.Display(root))
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Created += (_, e) => Touch(e.FullPath);
            watcher.Deleted += (_, e) => Touch(e.FullPath);
            watcher.Changed += (_, e) => Touch(e.FullPath);
            watcher.Renamed += (_, e) => { Touch(e.OldFullPath); Touch(e.FullPath); };
            watcher.Error += (_, _) => { lock (_touched) _lostRoots.Add(root); }; // notifications were lost
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Location missing or not watchable: it is indexed without live updates.
        }
    }

    void Touch(string path)
    {
        lock (_touched)
        {
            if (_touched.Count < MaxQueuedChanges) _touched.Add(path);
            _replay?.Add(path);
        }
    }

    // ---- applying changes --------------------------------------------------------------

    void SchedulePoll(TimeSpan? delay = null)
    {
        if (_disposed) return;
        try { _pollTimer.Change(delay ?? (Relaxed ? RelaxedPollInterval : PollInterval), Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { } // shutting down
    }

    void Poll()
    {
        if (_disposed || _fullBuild) return;
        if (!Monitor.TryEnter(_work)) return; // busy: the next tick will do
        var lost = new List<string>();
        try
        {
            if (_disposed || _fullBuild) return;
            var index = _index;
            var scope = _scope;
            bool changed = false;

            // 1. Journals: which folders changed since last time?
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (volume, journal) in _journals)
            {
                if (!_positions.TryGetValue(volume, out long position)) continue;
                long before = position;
                if (journal.Read(ref position, folders))
                {
                    _positions[volume] = position;
                    _dirty |= position != before;
                }
                else
                {
                    // The journal no longer holds our position: those locations must be looked at afresh.
                    _positions.Remove(volume);
                    lost.AddRange(scope.Roots.Where(r => string.Equals(VolumeOf(r), volume, StringComparison.OrdinalIgnoreCase)));
                }
            }
            changed |= SyncFolders(index, scope, folders);

            // 2. Notifications from locations without a journal.
            string[] paths;
            lock (_touched)
            {
                paths = [.. _touched];
                _touched.Clear();
                lost.AddRange(_lostRoots);
                _lostRoots.Clear();
            }
            var changes = Reconcile(index, scope, paths);
            if (changes.Count > 0)
            {
                IndexBuilder.ScanNew(index, index.Apply(changes, out bool applied), scope);
                changed |= applied;
            }

            if (changed)
            {
                _dirty = true;
                Interlocked.Increment(ref _changeCounter);
            }

            // 3. Housekeeping.
            if (index.DeadCount > Math.Max(50_000, index.Count / 4)) _index = index.Compact();
            if (_dirty && DateTime.UtcNow - _lastSave > SaveInterval) Save();
            if (changed) Changed?.Invoke();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            // Something changed again under us; the next round will pick it up.
        }
        finally
        {
            Monitor.Exit(_work);
            if (!_indexing) SchedulePoll();
        }

        if (lost.Count > 0 && !_indexing && !_disposed)
        {
            CancellationToken cancel;
            IndexScope scope;
            lock (_sync)
            {
                cancel = _cancel.Token;
                scope = _scope;
                _indexing = true;
            }
            Changed?.Invoke();
            RunJob(() => { foreach (var root in lost.Distinct(StringComparer.OrdinalIgnoreCase)) RescanRoot(scope, root, cancel); }, cancel);
        }
    }

    /// <summary>Re-lists the given folders and reconciles the index with what they contain now.</summary>
    static bool SyncFolders(FileIndex index, IndexScope scope, HashSet<string> folders)
    {
        if (folders.Count == 0) return false;
        bool anyChange = false;
        var reader = new FolderReader(scope);
        var batch = new List<FolderSnapshot>(Math.Min(folders.Count, SyncBatch));

        void Flush()
        {
            if (batch.Count == 0) return;
            var newFolders = index.SyncFolders(batch, out bool changed);
            IndexBuilder.ScanNew(index, newFolders, scope);
            anyChange |= changed;
            batch.Clear();
        }

        foreach (var folder in folders)
        {
            int entry = index.Resolve(folder);
            if (entry < 0) continue; // excluded, or outside the indexed locations
            try
            {
                reader.Read(folder);
                batch.Add(reader.ToSnapshot(entry));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue; // gone or unreadable; if it was deleted, its parent is in the list as well
            }
            if (batch.Count >= SyncBatch) Flush();
        }
        Flush();
        return anyChange;
    }

    /// <summary>Looks up what each changed path is on disk right now. Paths outside the index are dropped.</summary>
    static List<PendingChange> Reconcile(FileIndex index, IndexScope scope, string[] paths)
    {
        var changes = new List<PendingChange>(paths.Length);
        var parents = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in paths)
        {
            var path = raw.TrimEnd('\\');
            int cut = path.LastIndexOf('\\');
            if (cut < 2 || cut == path.Length - 1) continue;

            var folder = path[..cut];
            var name = path[(cut + 1)..];
            if (!parents.TryGetValue(folder, out int parent))
                parents[folder] = parent = index.Resolve(folder);
            if (parent < 0) continue;

            var change = new PendingChange { FullPath = path, Name = name, Parent = parent };
            try
            {
                var info = new FileInfo(path);
                var attributes = info.Attributes;
                if ((int)attributes != -1)
                {
                    bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                    if (isDirectory && (scope.IsExcludedName(name) || scope.IsExcludedPath(path))) continue;

                    change.Exists = true;
                    change.Size = isDirectory ? 0 : info.Length;
                    change.Modified = info.LastWriteTimeUtc.Ticks;
                    if (isDirectory) change.Flags |= EntryFlags.Directory;
                    if ((attributes & FileAttributes.Hidden) != 0) change.Flags |= EntryFlags.Hidden;
                    if ((attributes & FileAttributes.System) != 0) change.Flags |= EntryFlags.System;
                    if ((attributes & FileAttributes.ReparsePoint) != 0) change.Flags |= EntryFlags.Reparse;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue; // locked or mid-change: leave the entry as it is
            }
            changes.Add(change);
        }
        return changes;
    }

    // ---- persistence -------------------------------------------------------------------

    /// <summary>Writes the index with its journal positions. Caller holds <see cref="_work"/>.</summary>
    void Save()
    {
        var index = _index;
        index.Checkpoints.Clear();
        foreach (var (volume, position) in _positions)
            index.Checkpoints[volume] = (_journals[volume].JournalId, position);
        try
        {
            index.Save(_cacheFile);
            _dirty = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        _lastSave = DateTime.UtcNow;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _cancel.Cancel();
        }
        _pollTimer.Dispose();
        lock (_work)
        {
            // Keep what was learned since the last save, so the next start has less to catch up on.
            if (_dirty && !_fullBuild && _index.Count > 0) Save();
            CloseChangeSources();
        }
    }
}
