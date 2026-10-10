using System.Diagnostics;

namespace EasySearch.Core;

public sealed record IndexStatus(bool Indexing, int Items, int Scanned, long Version, DateTime? LastIndexedUtc, double LastDurationMs);

/// <summary>
/// Owns the live index: loads the cached copy for an instant start, rebuilds it in the
/// background, and keeps it current from file system notifications.
/// </summary>
public sealed class IndexService : IDisposable
{
    const int MaxQueuedChanges = 50_000;
    static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(800);

    readonly string _cacheFile;
    readonly object _sync = new();
    readonly object _buildLock = new();
    readonly HashSet<string> _touched = new(StringComparer.OrdinalIgnoreCase);
    readonly List<FileSystemWatcher> _watchers = [];
    readonly Timer _flushTimer;

    volatile FileIndex _index = new("");
    volatile FileIndex? _building;
    volatile IndexScope _scope = new([], []);
    CancellationTokenSource _cancel = new();
    volatile bool _indexing;
    volatile bool _rebuildRequested;
    DateTime? _lastIndexedUtc;
    double _lastDurationMs;
    long _changeCounter;
    bool _disposed;

    public IndexService(string cacheFile)
    {
        _cacheFile = cacheFile;
        _flushTimer = new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Raised (on a background thread) whenever the status or the index contents change.</summary>
    public event Action? Changed;

    public FileIndex Index => _index;

    public IndexStatus Status
    {
        get
        {
            var index = _index;
            return new IndexStatus(_indexing, index.LiveCount, _building?.Count ?? 0,
                Interlocked.Read(ref _changeCounter), _lastIndexedUtc, _lastDurationMs);
        }
    }

    /// <summary>Makes the cached index searchable right away (when it fits the scope), then refreshes it.</summary>
    public void Start(IndexScope scope)
    {
        var cached = FileIndex.TryLoad(_cacheFile, scope.Signature);
        if (cached is not null)
        {
            _index = cached;
            try { _lastIndexedUtc = File.GetLastWriteTimeUtc(_cacheFile); } catch (IOException) { }
        }
        Rebuild(scope, background: cached is not null);
    }

    /// <summary>Scans everything again. The current index stays searchable until the new one is ready.</summary>
    public void Rebuild(IndexScope scope, bool background = false)
    {
        CancellationToken cancel;
        lock (_sync)
        {
            if (_disposed) return;
            _cancel.Cancel();
            _cancel = new CancellationTokenSource();
            cancel = _cancel.Token;
            _scope = scope;
            _indexing = true;
        }
        Changed?.Invoke();

        var thread = new Thread(() => Build(scope, background, cancel)) { IsBackground = true, Name = "EasySearch index build" };
        thread.Start();
    }

    void Build(IndexScope scope, bool background, CancellationToken cancel)
    {
        lock (_buildLock)
        {
            if (cancel.IsCancellationRequested) return;
            try
            {
                // Watch first, so changes made during the scan are queued and replayed afterwards.
                StartWatchers(scope);

                var timer = Stopwatch.StartNew();
                var built = IndexBuilder.Build(scope, cancel,
                    workers: background ? Math.Clamp(Environment.ProcessorCount / 2, 2, 4) : 0,
                    onCreated: index =>
                    {
                        _building = index;
                        // Nothing useful to show yet (first run or new scope): search the index as it fills up.
                        if (_index.Count == 0 || _index.Signature != index.Signature) _index = index;
                    },
                    priority: background ? ThreadPriority.BelowNormal : ThreadPriority.Normal);
                if (cancel.IsCancellationRequested) return;

                _index = built;
                Interlocked.Increment(ref _changeCounter);
                _lastIndexedUtc = DateTime.UtcNow;
                _lastDurationMs = timer.Elapsed.TotalMilliseconds;

                try { built.Save(_cacheFile); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            finally
            {
                _building = null;
                if (!cancel.IsCancellationRequested)
                {
                    _indexing = false;
                    _flushTimer.Change(FlushInterval, Timeout.InfiniteTimeSpan);
                }
                Changed?.Invoke();
            }
        }
    }

    // ---- live updates ------------------------------------------------------------------

    void StartWatchers(IndexScope scope)
    {
        lock (_watchers)
        {
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
            lock (_touched) _touched.Clear();

            foreach (var root in scope.Roots)
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
                    watcher.Error += (_, _) => _rebuildRequested = true; // notifications were lost
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
                catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    // Location missing or not watchable (some network/removable drives): index it without live updates.
                }
            }
        }
    }

    void Touch(string path)
    {
        lock (_touched)
        {
            if (_touched.Count < MaxQueuedChanges) _touched.Add(path);
            else _rebuildRequested = true;
        }
    }

    void Flush()
    {
        if (_disposed) return;
        try
        {
            if (_indexing) return; // the build reschedules the flush when it finishes

            if (_rebuildRequested)
            {
                _rebuildRequested = false;
                Rebuild(_scope, background: true);
                return;
            }

            string[] paths;
            lock (_touched)
            {
                if (_touched.Count == 0) return;
                paths = [.. _touched];
                _touched.Clear();
            }

            var index = _index;
            var scope = _scope;
            var changes = Reconcile(index, scope, paths);
            if (changes.Count == 0) return;

            var newFolders = index.Apply(changes);
            if (newFolders.Count > 0) IndexBuilder.Scan(index, newFolders, scope, workers: 1);
            Interlocked.Increment(ref _changeCounter);
            Changed?.Invoke();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A path changed again under us; the next notification will pick it up.
        }
        finally
        {
            if (!_disposed && !_indexing) _flushTimer.Change(FlushInterval, Timeout.InfiniteTimeSpan);
        }
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

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _cancel.Cancel();
        }
        _flushTimer.Dispose();
        lock (_watchers)
        {
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
        }
    }
}
