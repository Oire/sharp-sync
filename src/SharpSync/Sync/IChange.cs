namespace Oire.SharpSync.Sync;

/// <summary>
/// Represents a change detected during synchronization
/// </summary>
internal interface IChange {
    string Path { get; }
}
