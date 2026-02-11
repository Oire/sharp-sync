using Oire.SharpSync.Database;

namespace Oire.SharpSync.Sync;

/// <summary>
/// Represents a deletion of a file or directory
/// </summary>
internal sealed record DeletionChange(string Path, bool DeletedLocally, bool DeletedRemotely, SyncState TrackedState): IChange;
