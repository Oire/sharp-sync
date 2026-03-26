using Oire.SharpSync.Core;

namespace Oire.SharpSync.Sync;

/// <summary>
/// Thread-safe wrapper for SyncResult counters.
/// Call <see cref="Flush"/> after all parallel processing completes
/// to write the final values to the underlying <see cref="SyncResult"/>.
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

    public void IncrementFilesSynchronized() => Interlocked.Increment(ref _filesSynchronized);

    public void IncrementFilesSkipped() => Interlocked.Increment(ref _filesSkipped);

    public void IncrementFilesConflicted() => Interlocked.Increment(ref _filesConflicted);

    public void IncrementFilesDeleted() => Interlocked.Increment(ref _filesDeleted);

    /// <summary>
    /// Writes the final atomic counter values to the underlying <see cref="SyncResult"/>.
    /// Must be called after all parallel processing has completed.
    /// </summary>
    public void Flush() {
        _result.FilesSynchronized = Interlocked.Read(ref _filesSynchronized);
        _result.FilesSkipped = Interlocked.Read(ref _filesSkipped);
        _result.FilesConflicted = Interlocked.Read(ref _filesConflicted);
        _result.FilesDeleted = Interlocked.Read(ref _filesDeleted);
    }
}
