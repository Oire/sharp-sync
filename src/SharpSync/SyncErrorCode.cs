namespace Oire.SharpSync;

/// <summary>
/// Error codes for SharpSync operations
/// </summary>
public enum SyncErrorCode
{
    /// <summary>
    /// Operation completed successfully
    /// </summary>
    Success = 0,

    /// <summary>
    /// Generic error
    /// </summary>
    Generic = 1,

    /// <summary>
    /// Out of memory
    /// </summary>
    OutOfMemory = 2,

    /// <summary>
    /// Operation not supported
    /// </summary>
    NotSupported = 3,

    /// <summary>
    /// Invalid file or directory path
    /// </summary>
    InvalidPath = 4,

    /// <summary>
    /// Permission denied
    /// </summary>
    PermissionDenied = 5,

    /// <summary>
    /// File not found
    /// </summary>
    FileNotFound = 6,

    /// <summary>
    /// File already exists
    /// </summary>
    FileExists = 7,

    /// <summary>
    /// File or directory is read-only
    /// </summary>
    ReadOnly = 8,

    /// <summary>
    /// File conflict detected
    /// </summary>
    Conflict = 9,

    /// <summary>
    /// Operation timed out
    /// </summary>
    Timeout = 10
}