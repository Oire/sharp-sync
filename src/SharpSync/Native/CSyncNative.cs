using System.Runtime.InteropServices;

namespace Oire.SharpSync.Native;

/// <summary>
/// Native P/Invoke declarations for CSync library
/// </summary>
internal static class CSyncNative
{
    internal const string CSyncLibrary = "csync";
    
    static CSyncNative()
    {
        NativeLibraryLoader.SetDllImportResolver();
    }

    /// <summary>
    /// CSync error codes
    /// </summary>
    internal enum CSyncError : int
    {
        Success = 0,
        ErrorGeneric = 1,
        ErrorNoMemory = 2,
        ErrorNotSupported = 3,
        ErrorInvalidPath = 4,
        ErrorPermissionDenied = 5,
        ErrorFileNotFound = 6,
        ErrorFileExists = 7,
        ErrorReadOnly = 8,
        ErrorConflict = 9,
        ErrorTimeout = 10
    }

    /// <summary>
    /// CSync synchronization options
    /// </summary>
    [Flags]
    internal enum CSyncOptions : uint
    {
        None = 0,
        PreservePermissions = 1 << 0,
        PreserveTimestamps = 1 << 1,
        FollowSymlinks = 1 << 2,
        DryRun = 1 << 3,
        Verbose = 1 << 4,
        ChecksumOnly = 1 << 5,
        SizeOnly = 1 << 6,
        DeleteExtraneous = 1 << 7,
        UpdateExisting = 1 << 8
    }

    /// <summary>
    /// Progress callback delegate
    /// </summary>
    /// <param name="current">Current file number</param>
    /// <param name="total">Total files to process</param>
    /// <param name="filename">Current filename</param>
    /// <param name="userData">User data pointer</param>
    /// <returns>0 to continue, non-zero to abort</returns>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ProgressCallback(long current, long total, IntPtr filename, IntPtr userData);

    /// <summary>
    /// Conflict callback delegate
    /// </summary>
    /// <param name="conflictType">Type of conflict</param>
    /// <param name="sourcePath">Source file path</param>
    /// <param name="targetPath">Target file path</param>
    /// <param name="userData">User data pointer</param>
    /// <returns>Resolution action</returns>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ConflictCallback(int conflictType, IntPtr sourcePath, IntPtr targetPath, IntPtr userData);

    /// <summary>
    /// Initialize CSync context
    /// </summary>
    /// <returns>Context handle or null on failure</returns>
    [DllImport(CSyncLibrary, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr csync_create();

    /// <summary>
    /// Destroy CSync context
    /// </summary>
    /// <param name="ctx">Context handle</param>
    [DllImport(CSyncLibrary, CallingConvention = CallingConvention.Cdecl)]
    public static extern void csync_destroy(IntPtr ctx);

    /// <summary>
    /// Set source directory
    /// </summary>
    /// <param name="ctx">Context handle</param>
    /// <param name="path">Source directory path</param>
    /// <returns>Error code</returns>
    [DllImport(CSyncLibrary, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern CSyncError csync_set_source(IntPtr ctx, string path);

    /// <summary>
    /// Set target directory
    /// </summary>
    /// <param name="ctx">Context handle</param>
    /// <param name="path">Target directory path</param>
    /// <returns>Error code</returns>
    [DllImport(CSyncLibrary, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern CSyncError csync_set_target(IntPtr ctx, string path);

    /// <summary>
    /// Set synchronization options
    /// </summary>
    /// <param name="ctx">Context handle</param>
    /// <param name="options">Sync options</param>
    /// <returns>Error code</returns>
    [DllImport(CSyncLibrary, CallingConvention = CallingConvention.Cdecl)]
    public static extern CSyncError csync_set_options(IntPtr ctx, CSyncOptions options);

    /// <summary>
    /// Set progress callback
    /// </summary>
    /// <param name="ctx">Context handle</param>
    /// <param name="callback">Progress callback function</param>
    /// <param name="userData">User data pointer</param>
    /// <returns>Error code</returns>
    [DllImport(CSyncLibrary, CallingConvention = CallingConvention.Cdecl)]
    public static extern CSyncError csync_set_progress_callback(IntPtr ctx, ProgressCallback callback, IntPtr userData);

    /// <summary>
    /// Set conflict callback
    /// </summary>
    /// <param name="ctx">Context handle</param>
    /// <param name="callback">Conflict callback function</param>
    /// <param name="userData">User data pointer</param>
    /// <returns>Error code</returns>
    [DllImport(CSyncLibrary, CallingConvention = CallingConvention.Cdecl)]
    public static extern CSyncError csync_set_conflict_callback(IntPtr ctx, ConflictCallback callback, IntPtr userData);

    /// <summary>
    /// Start synchronization
    /// </summary>
    /// <param name="ctx">Context handle</param>
    /// <returns>Error code</returns>
    [DllImport(CSyncLibrary, CallingConvention = CallingConvention.Cdecl)]
    public static extern CSyncError csync_sync(IntPtr ctx);

    /// <summary>
    /// Get last error message
    /// </summary>
    /// <param name="ctx">Context handle</param>
    /// <returns>Error message pointer</returns>
    [DllImport(CSyncLibrary, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr csync_get_error_string(IntPtr ctx);

    /// <summary>
    /// Get CSync library version
    /// </summary>
    /// <returns>Version string pointer</returns>
    [DllImport(CSyncLibrary, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr csync_get_version();
}