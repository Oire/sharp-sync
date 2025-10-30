using Oire.SharpSync.Core;

namespace Oire.SharpSync.Sync;

/// <summary>
/// Thread-safe wrapper for SyncResult counters
/// </summary>
internal sealed class ThreadSafeSyncResult {
    private readonly SyncResult _result;
    private long _filesSynchronized;
    private long _filesSkipped;
    private long _filesConflicted;
    private long _filesDeleted;

    public ThreadSafeSyncResult(SyncResult result) {
        _result = result;
    }

    public void IncrementFilesSynchronized() {
        var newValue = Interlocked.Increment(ref _filesSynchronized);
        _result.FilesSynchronized = newValue;
    }

    public void IncrementFilesSkipped() {
        var newValue = Interlocked.Increment(ref _filesSkipped);
        _result.FilesSkipped = newValue;
    }

    public void IncrementFilesConflicted() {
        var newValue = Interlocked.Increment(ref _filesConflicted);
        _result.FilesConflicted = newValue;
    }

    public void IncrementFilesDeleted() {
        var newValue = Interlocked.Increment(ref _filesDeleted);
        _result.FilesDeleted = newValue;
    }
}
