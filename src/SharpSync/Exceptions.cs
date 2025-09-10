using Oire.SharpSync.Native;

namespace Oire.SharpSync;

/// <summary>
/// Base exception for all SharpSync operations
/// </summary>
public class SyncException : Exception
{
    /// <summary>
    /// The SharpSync error code
    /// </summary>
    public SyncErrorCode ErrorCode { get; }

    /// <summary>
    /// Initializes a new instance of the SyncException class
    /// </summary>
    /// <param name="errorCode">The sync error code</param>
    /// <param name="message">The error message</param>
    public SyncException(SyncErrorCode errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new instance of the SyncException class
    /// </summary>
    /// <param name="errorCode">The sync error code</param>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception</param>
    public SyncException(SyncErrorCode errorCode, string message, Exception innerException) : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Internal constructor for converting from native error codes
    /// </summary>
    /// <param name="nativeError">The native CSync error code</param>
    /// <param name="message">The error message</param>
    internal SyncException(CSyncNative.CSyncError nativeError, string message) : base(message)
    {
        ErrorCode = ConvertFromNativeError(nativeError);
    }

    private static SyncErrorCode ConvertFromNativeError(CSyncNative.CSyncError nativeError)
    {
        return nativeError switch
        {
            CSyncNative.CSyncError.Success => SyncErrorCode.Success,
            CSyncNative.CSyncError.ErrorGeneric => SyncErrorCode.Generic,
            CSyncNative.CSyncError.ErrorNoMemory => SyncErrorCode.OutOfMemory,
            CSyncNative.CSyncError.ErrorNotSupported => SyncErrorCode.NotSupported,
            CSyncNative.CSyncError.ErrorInvalidPath => SyncErrorCode.InvalidPath,
            CSyncNative.CSyncError.ErrorPermissionDenied => SyncErrorCode.PermissionDenied,
            CSyncNative.CSyncError.ErrorFileNotFound => SyncErrorCode.FileNotFound,
            CSyncNative.CSyncError.ErrorFileExists => SyncErrorCode.FileExists,
            CSyncNative.CSyncError.ErrorReadOnly => SyncErrorCode.ReadOnly,
            CSyncNative.CSyncError.ErrorConflict => SyncErrorCode.Conflict,
            CSyncNative.CSyncError.ErrorTimeout => SyncErrorCode.Timeout,
            _ => SyncErrorCode.Generic
        };
    }
}

/// <summary>
/// Exception thrown when a file or directory path is invalid
/// </summary>
public class InvalidPathException : SyncException
{
    /// <summary>
    /// The invalid path
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Initializes a new instance of the InvalidPathException class
    /// </summary>
    /// <param name="path">The invalid path</param>
    /// <param name="message">The error message</param>
    public InvalidPathException(string path, string message) 
        : base(SyncErrorCode.InvalidPath, message)
    {
        Path = path;
    }
}

/// <summary>
/// Exception thrown when access to a file or directory is denied
/// </summary>
public class PermissionDeniedException : SyncException
{
    /// <summary>
    /// The path where permission was denied
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Initializes a new instance of the PermissionDeniedException class
    /// </summary>
    /// <param name="path">The path where permission was denied</param>
    /// <param name="message">The error message</param>
    public PermissionDeniedException(string path, string message) 
        : base(SyncErrorCode.PermissionDenied, message)
    {
        Path = path;
    }
}

/// <summary>
/// Exception thrown when a file conflict is detected during synchronization
/// </summary>
public class FileConflictException : SyncException
{
    /// <summary>
    /// The source file path involved in the conflict
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// The target file path involved in the conflict
    /// </summary>
    public string TargetPath { get; }

    /// <summary>
    /// Initializes a new instance of the FileConflictException class
    /// </summary>
    /// <param name="sourcePath">The source file path</param>
    /// <param name="targetPath">The target file path</param>
    /// <param name="message">The error message</param>
    public FileConflictException(string sourcePath, string targetPath, string message) 
        : base(SyncErrorCode.Conflict, message)
    {
        SourcePath = sourcePath;
        TargetPath = targetPath;
    }
}

/// <summary>
/// Exception thrown when a file is not found
/// </summary>
public class FileNotFoundException : SyncException
{
    /// <summary>
    /// The file path that was not found
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Initializes a new instance of the FileNotFoundException class
    /// </summary>
    /// <param name="fileName">The file that was not found</param>
    /// <param name="message">The error message</param>
    public FileNotFoundException(string fileName, string message) 
        : base(SyncErrorCode.FileNotFound, message)
    {
        FileName = fileName;
    }
}