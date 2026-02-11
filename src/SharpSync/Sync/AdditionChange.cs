using Oire.SharpSync.Core;

namespace Oire.SharpSync.Sync;

/// <summary>
/// Represents a new file or directory addition
/// </summary>
internal sealed record AdditionChange(string Path, SyncItem Item, bool IsLocal): IChange;
