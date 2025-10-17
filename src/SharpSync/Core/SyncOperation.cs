namespace Oire.SharpSync.Core;

/// <summary>
/// Types of sync operations
/// </summary>
public enum SyncOperation
{
    Unknown,
    Scanning,
    Uploading,
    Downloading,
    Deleting,
    CreatingDirectory,
    ResolvingConflict
}