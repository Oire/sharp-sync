using Oire.SharpSync.Core;
using Oire.SharpSync.Database;

namespace Oire.SharpSync.Sync;

/// <summary>
/// Represents a modification to an existing file or directory
/// </summary>
internal sealed record ModificationChange(string Path, SyncItem Item, bool IsLocal, SyncState TrackedState): IChange;
