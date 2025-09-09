using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpSync.Native;

namespace SharpSync;

/// <summary>
/// High-level synchronization engine that wraps CSync functionality
/// </summary>
public class SyncEngine : IDisposable
{
    private IntPtr _context;
    private bool _disposed;
    private CSyncNative.ProgressCallback? _progressCallback;
    private CSyncNative.ConflictCallback? _conflictCallback;
    private GCHandle _progressCallbackHandle;
    private GCHandle _conflictCallbackHandle;

    /// <summary>
    /// Event raised to report synchronization progress
    /// </summary>
    public event EventHandler<SyncProgress>? ProgressChanged;

    /// <summary>
    /// Event raised when a file conflict is detected
    /// </summary>
    public event EventHandler<FileConflictEventArgs>? ConflictDetected;

    /// <summary>
    /// Gets whether the engine is currently synchronizing
    /// </summary>
    public bool IsSynchronizing { get; private set; }

    /// <summary>
    /// Gets the CSync library version
    /// </summary>
    public static string LibraryVersion
    {
        get
        {
            try
            {
                var versionPtr = CSyncNative.csync_get_version();
                return versionPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(versionPtr) ?? "Unknown" : "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the SyncEngine class
    /// </summary>
    /// <exception cref="SyncException">Thrown when the CSync context cannot be created</exception>
    public SyncEngine()
    {
        _context = CSyncNative.csync_create();
        if (_context == IntPtr.Zero)
        {
            throw new SyncException(SyncErrorCode.OutOfMemory, "Failed to create CSync context");
        }

        SetupCallbacks();
    }

    /// <summary>
    /// Synchronizes files between source and target directories
    /// </summary>
    /// <param name="sourcePath">The source directory path</param>
    /// <param name="targetPath">The target directory path</param>
    /// <param name="options">Synchronization options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The synchronization result</returns>
    /// <exception cref="ArgumentException">Thrown when paths are invalid</exception>
    /// <exception cref="SyncException">Thrown when synchronization fails</exception>
    public async Task<SyncResult> SynchronizeAsync(string sourcePath, string targetPath, SyncOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path cannot be null or empty", nameof(sourcePath));
        
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("Target path cannot be null or empty", nameof(targetPath));

        if (_disposed)
            throw new ObjectDisposedException(nameof(SyncEngine));

        if (IsSynchronizing)
            throw new InvalidOperationException("Synchronization is already in progress");

        options ??= new SyncOptions();
        var result = new SyncResult();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            IsSynchronizing = true;

            // Validate paths
            if (!Directory.Exists(sourcePath))
                throw new InvalidPathException(sourcePath, $"Source directory does not exist: {sourcePath}");

            // Create target directory if it doesn't exist
            if (!Directory.Exists(targetPath))
            {
                try
                {
                    Directory.CreateDirectory(targetPath);
                }
                catch (Exception ex)
                {
                    throw new PermissionDeniedException(targetPath, $"Cannot create target directory: {targetPath}. {ex.Message}");
                }
            }

            // Configure CSync
            await ConfigureSyncAsync(sourcePath, targetPath, options, cancellationToken);

            // Perform synchronization
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var error = CSyncNative.csync_sync(_context);
                if (error != CSyncNative.CSyncError.Success)
                {
                    var errorMsg = GetErrorMessage();
                    throw CreateExceptionFromError(error, errorMsg);
                }
            }, cancellationToken);

            result.Success = true;
            result.Details = "Synchronization completed successfully";
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.Error = new OperationCanceledException("Synchronization was cancelled");
            result.Details = "Synchronization was cancelled by user";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex;
            result.Details = ex.Message;
        }
        finally
        {
            IsSynchronizing = false;
            stopwatch.Stop();
            result.ElapsedTime = stopwatch.Elapsed;
        }

        return result;
    }

    /// <summary>
    /// Synchronizes files between source and target directories (synchronous version)
    /// </summary>
    /// <param name="sourcePath">The source directory path</param>
    /// <param name="targetPath">The target directory path</param>
    /// <param name="options">Synchronization options</param>
    /// <returns>The synchronization result</returns>
    public SyncResult Synchronize(string sourcePath, string targetPath, SyncOptions? options = null)
    {
        return SynchronizeAsync(sourcePath, targetPath, options).GetAwaiter().GetResult();
    }

    private async Task ConfigureSyncAsync(string sourcePath, string targetPath, SyncOptions options, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var error = CSyncNative.csync_set_source(_context, sourcePath);
            if (error != CSyncNative.CSyncError.Success)
                throw CreateExceptionFromError(error, $"Failed to set source path: {sourcePath}");

            error = CSyncNative.csync_set_target(_context, targetPath);
            if (error != CSyncNative.CSyncError.Success)
                throw CreateExceptionFromError(error, $"Failed to set target path: {targetPath}");

            var nativeOptions = ConvertToNativeOptions(options);
            error = CSyncNative.csync_set_options(_context, nativeOptions);
            if (error != CSyncNative.CSyncError.Success)
                throw CreateExceptionFromError(error, "Failed to set synchronization options");
        }, cancellationToken);
    }

    private CSyncNative.CSyncOptions ConvertToNativeOptions(SyncOptions options)
    {
        var nativeOptions = CSyncNative.CSyncOptions.None;

        if (options.PreservePermissions)
            nativeOptions |= CSyncNative.CSyncOptions.PreservePermissions;
        
        if (options.PreserveTimestamps)
            nativeOptions |= CSyncNative.CSyncOptions.PreserveTimestamps;
        
        if (options.FollowSymlinks)
            nativeOptions |= CSyncNative.CSyncOptions.FollowSymlinks;
        
        if (options.DryRun)
            nativeOptions |= CSyncNative.CSyncOptions.DryRun;
        
        if (options.Verbose)
            nativeOptions |= CSyncNative.CSyncOptions.Verbose;
        
        if (options.ChecksumOnly)
            nativeOptions |= CSyncNative.CSyncOptions.ChecksumOnly;
        
        if (options.SizeOnly)
            nativeOptions |= CSyncNative.CSyncOptions.SizeOnly;
        
        if (options.DeleteExtraneous)
            nativeOptions |= CSyncNative.CSyncOptions.DeleteExtraneous;
        
        if (options.UpdateExisting)
            nativeOptions |= CSyncNative.CSyncOptions.UpdateExisting;

        return nativeOptions;
    }

    private void SetupCallbacks()
    {
        _progressCallback = OnProgressCallback;
        _conflictCallback = OnConflictCallback;

        _progressCallbackHandle = GCHandle.Alloc(_progressCallback);
        _conflictCallbackHandle = GCHandle.Alloc(_conflictCallback);

        CSyncNative.csync_set_progress_callback(_context, _progressCallback, IntPtr.Zero);
        CSyncNative.csync_set_conflict_callback(_context, _conflictCallback, IntPtr.Zero);
    }

    private int OnProgressCallback(long current, long total, IntPtr filenamePtr, IntPtr userData)
    {
        try
        {
            var filename = filenamePtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(filenamePtr) ?? string.Empty : string.Empty;
            
            var progress = new SyncProgress
            {
                CurrentFile = current,
                TotalFiles = total,
                CurrentFileName = filename
            };

            ProgressChanged?.Invoke(this, progress);
            return progress.IsCancelled ? 1 : 0;
        }
        catch
        {
            return 1; // Error occurred, abort
        }
    }

    private int OnConflictCallback(int conflictType, IntPtr sourcePathPtr, IntPtr targetPathPtr, IntPtr userData)
    {
        try
        {
            var sourcePath = sourcePathPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(sourcePathPtr) ?? string.Empty : string.Empty;
            var targetPath = targetPathPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(targetPathPtr) ?? string.Empty : string.Empty;

            var args = new FileConflictEventArgs(sourcePath, targetPath, (ConflictType)conflictType);
            ConflictDetected?.Invoke(this, args);

            return (int)args.Resolution;
        }
        catch
        {
            return (int)ConflictResolution.Skip; // Error occurred, skip the file
        }
    }

    private string GetErrorMessage()
    {
        try
        {
            var errorPtr = CSyncNative.csync_get_error_string(_context);
            return errorPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(errorPtr) ?? "Unknown error" : "Unknown error";
        }
        catch
        {
            return "Unknown error";
        }
    }

    private static SyncException CreateExceptionFromError(CSyncNative.CSyncError error, string message)
    {
        return error switch
        {
            CSyncNative.CSyncError.ErrorInvalidPath => new InvalidPathException(string.Empty, message),
            CSyncNative.CSyncError.ErrorPermissionDenied => new PermissionDeniedException(string.Empty, message),
            CSyncNative.CSyncError.ErrorFileNotFound => new FileNotFoundException(string.Empty, message),
            CSyncNative.CSyncError.ErrorConflict => new FileConflictException(string.Empty, string.Empty, message),
            _ => new SyncException(error, message)
        };
    }

    /// <summary>
    /// Releases all resources used by the SyncEngine
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_context != IntPtr.Zero)
            {
                CSyncNative.csync_destroy(_context);
                _context = IntPtr.Zero;
            }

            if (_progressCallbackHandle.IsAllocated)
                _progressCallbackHandle.Free();

            if (_conflictCallbackHandle.IsAllocated)
                _conflictCallbackHandle.Free();

            _disposed = true;
        }
    }
}

/// <summary>
/// Event arguments for file conflict events
/// </summary>
public class FileConflictEventArgs : EventArgs
{
    /// <summary>
    /// Gets the source file path
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// Gets the target file path
    /// </summary>
    public string TargetPath { get; }

    /// <summary>
    /// Gets the type of conflict
    /// </summary>
    public ConflictType ConflictType { get; }

    /// <summary>
    /// Gets or sets the resolution for this conflict
    /// </summary>
    public ConflictResolution Resolution { get; set; }

    /// <summary>
    /// Initializes a new instance of the FileConflictEventArgs class
    /// </summary>
    /// <param name="sourcePath">The source file path</param>
    /// <param name="targetPath">The target file path</param>
    /// <param name="conflictType">The type of conflict</param>
    public FileConflictEventArgs(string sourcePath, string targetPath, ConflictType conflictType)
    {
        SourcePath = sourcePath;
        TargetPath = targetPath;
        ConflictType = conflictType;
        Resolution = ConflictResolution.Ask;
    }
}

/// <summary>
/// Types of file conflicts
/// </summary>
public enum ConflictType
{
    /// <summary>
    /// Both files have been modified since last sync
    /// </summary>
    BothModified = 1,

    /// <summary>
    /// File deleted in source but modified in target
    /// </summary>
    DeletedInSourceModifiedInTarget = 2,

    /// <summary>
    /// File modified in source but deleted in target
    /// </summary>
    ModifiedInSourceDeletedInTarget = 3,

    /// <summary>
    /// File exists in both locations but with different types (file vs directory)
    /// </summary>
    TypeConflict = 4
}