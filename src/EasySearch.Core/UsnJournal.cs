using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EasySearch.Core;

/// <summary>
/// Reads the NTFS change journal of one volume: Windows' own log of every create, delete, rename
/// and write. It is what lets the index pick up exactly what changed — also across restarts —
/// instead of scanning the drive again.
///
/// This uses the unprivileged read, which needs no administrator rights. Windows withholds file
/// names from it, but each record still says which folder the change happened in, and re-listing
/// just that folder is all the index needs.
/// </summary>
public sealed class UsnJournal : IDisposable
{
    const uint FSCTL_QUERY_USN_JOURNAL = 0x000900F4;
    const uint FSCTL_READ_UNPRIVILEGED_USN_JOURNAL = 0x000903AB;
    const uint SYNCHRONIZE = 0x00100000, FILE_TRAVERSE = 0x00000020;
    const uint ShareAll = 7, OPEN_EXISTING = 3, FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    const int ERROR_HANDLE_EOF = 38;
    const int BufferSize = 64 * 1024;

    // Create, delete, rename (old and new name), attribute change, and close (which carries the
    // final size and date of a file that was written). Intermediate write records are skipped.
    const uint Reasons = 0x00000100 | 0x00000200 | 0x00001000 | 0x00002000 | 0x00008000 | 0x80000000;

    readonly SafeFileHandle _volume;
    readonly IntPtr _input = Marshal.AllocHGlobal(48);
    readonly IntPtr _output = Marshal.AllocHGlobal(BufferSize);
    readonly char[] _path = new char[1024];

    UsnJournal(string volume, SafeFileHandle handle, long journalId)
    {
        Volume = volume;
        _volume = handle;
        JournalId = journalId;
    }

    /// <summary>Drive designator such as "C:".</summary>
    public string Volume { get; }

    /// <summary>Changes whenever the journal is recreated; positions from an older journal are meaningless.</summary>
    public long JournalId { get; private set; }

    /// <summary>Opens the journal of <paramref name="volume"/> ("C:"), or returns null when the drive has none (FAT, network, ...).</summary>
    public static UsnJournal? TryOpen(string volume)
    {
        if (!OperatingSystem.IsWindows() || volume.Length != 2 || volume[1] != ':') return null;

        var handle = CreateFileW(@"\\.\" + volume, SYNCHRONIZE | FILE_TRAVERSE, ShareAll, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        var journal = new UsnJournal(volume.ToUpperInvariant(), handle, 0);
        if (!journal.TryQuery(out long id, out _, out _))
        {
            journal.Dispose();
            return null;
        }
        journal.JournalId = id;
        return journal;
    }

    /// <summary>The range of positions the journal currently holds: [first, next).</summary>
    public bool TryQuery(out long first, out long next) => TryQuery(out _, out first, out next);

    bool TryQuery(out long journalId, out long first, out long next)
    {
        journalId = first = next = 0;
        if (!DeviceIoControl(_volume, FSCTL_QUERY_USN_JOURNAL, IntPtr.Zero, 0, _output, 80, out int returned, IntPtr.Zero) || returned < 24)
            return false;
        journalId = Marshal.ReadInt64(_output, 0);
        first = Marshal.ReadInt64(_output, 8);
        next = Marshal.ReadInt64(_output, 16);
        return true;
    }

    /// <summary>
    /// Reads everything from <paramref name="position"/> to the end of the journal, adding each
    /// folder in which something changed to <paramref name="changedFolders"/> (normalized paths)
    /// and advancing the position. Returns false when the journal no longer reaches back that
    /// far, in which case the caller has to look at the drive itself.
    /// </summary>
    public bool Read(ref long position, HashSet<string> changedFolders, CancellationToken cancel = default)
    {
        var folders = new Dictionary<long, string?>();
        while (!cancel.IsCancellationRequested)
        {
            // READ_USN_JOURNAL_DATA_V1
            Marshal.WriteInt64(_input, 0, position);
            Marshal.WriteInt32(_input, 8, unchecked((int)Reasons));
            Marshal.WriteInt32(_input, 12, 0);     // not only on close
            Marshal.WriteInt64(_input, 16, 0);     // no timeout: return at once
            Marshal.WriteInt64(_input, 24, 0);     // no minimum amount to wait for
            Marshal.WriteInt64(_input, 32, JournalId);
            Marshal.WriteInt16(_input, 40, 2);     // record versions 2..2
            Marshal.WriteInt16(_input, 42, 2);

            if (!DeviceIoControl(_volume, FSCTL_READ_UNPRIVILEGED_USN_JOURNAL, _input, 48, _output, BufferSize, out int returned, IntPtr.Zero))
                return Marshal.GetLastWin32Error() == ERROR_HANDLE_EOF;
            if (returned < 8) return false;

            long next = Marshal.ReadInt64(_output, 0);
            for (int offset = 8; offset + 60 <= returned;)
            {
                int length = Marshal.ReadInt32(_output, offset);
                if (length <= 0) break;

                long parent = Marshal.ReadInt64(_output, offset + 16);
                if (!folders.TryGetValue(parent, out var folder)) folders[parent] = folder = PathOf(parent);
                if (folder is not null) changedFolders.Add(folder);
                offset += length;
            }

            if (returned == 8 || next == position) { position = next; return true; } // caught up
            position = next;
        }
        return true;
    }

    /// <summary>Full path of the folder with this file reference number, or null when it is gone or off limits.</summary>
    string? PathOf(long fileReference)
    {
        var id = new FILE_ID_DESCRIPTOR { Size = 24, Type = 0, FileId = fileReference };
        using var folder = OpenFileById(_volume, ref id, 0, ShareAll, IntPtr.Zero, FILE_FLAG_BACKUP_SEMANTICS);
        if (folder.IsInvalid) return null;

        int length = GetFinalPathNameByHandleW(folder, _path, _path.Length, 0);
        if (length <= 0 || length >= _path.Length) return null;

        var path = _path.AsSpan(0, length);
        if (path.StartsWith(@"\\?\")) path = path[4..];
        path = path.TrimEnd('\\');
        return path.Length >= 2 && path[1] == ':' ? path.ToString() : null;
    }

    public void Dispose()
    {
        _volume.Dispose();
        Marshal.FreeHGlobal(_input);
        Marshal.FreeHGlobal(_output);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FILE_ID_DESCRIPTOR
    {
        public int Size;
        public int Type;
        public long FileId;
        public long Reserved;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern SafeFileHandle OpenFileById(SafeFileHandle volumeHint, ref FILE_ID_DESCRIPTOR id, uint access, uint share, IntPtr security, uint flags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern int GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] path, int capacity, int flags);
}
