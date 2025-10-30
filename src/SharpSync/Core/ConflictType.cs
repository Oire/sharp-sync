namespace Oire.SharpSync.Core;

/// <summary>
/// Types of file conflicts
/// </summary>
public enum ConflictType {
    /// <summary>
    /// Both files have been modified since last sync
    /// </summary>
    BothModified,

    /// <summary>
    /// File deleted locally but modified remotely
    /// </summary>
    DeletedLocallyModifiedRemotely,

    /// <summary>
    /// File modified locally but deleted remotely
    /// </summary>
    ModifiedLocallyDeletedRemotely,

    /// <summary>
    /// File exists in both locations but with different types (file vs directory)
    /// </summary>
    TypeConflict,

    /// <summary>
    /// Both sides created a new file with the same name
    /// </summary>
    BothCreated
}
