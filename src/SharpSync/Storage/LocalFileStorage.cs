using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Oire.SharpSync.Core;

namespace Oire.SharpSync.Storage;

/// <summary>
/// Local filesystem storage implementation
/// </summary>
public class LocalFileStorage: ISyncStorage {
    /// <summary>
    /// Event raised to report transfer progress for file operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This event is not typically raised by local file storage because local
    /// filesystem operations are very fast compared to network transfers.
    /// </para>
    /// <para>
    /// The event is implemented to satisfy the <see cref="ISyncStorage"/> interface,
    /// allowing consistent handling across all storage types.
    /// </para>
    /// </remarks>
#pragma warning disable CS0067 // Event is never used (intentional - local storage doesn't report progress)
    public event EventHandler<StorageProgressEventArgs>? ProgressChanged;
#pragma warning restore CS0067

    /// <summary>
    /// Gets the storage type (always returns <see cref="Core.StorageType.Local"/>)
    /// </summary>
    public StorageType StorageType => StorageType.Local;

    /// <summary>
    /// Gets the root path on the local filesystem
    /// </summary>
    public string RootPath { get; }

    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new local file storage instance
    /// </summary>
    /// <param name="rootPath">The root directory path for synchronization</param>
    /// <param name="logger">Optional logger for diagnostic output</param>
    /// <exception cref="ArgumentException">Thrown when rootPath is null or empty</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the root path does not exist</exception>
    public LocalFileStorage(string rootPath, ILogger? logger = null) {
        if (string.IsNullOrWhiteSpace(rootPath)) {
            throw new ArgumentException("Root path cannot be empty", nameof(rootPath));
        }

        RootPath = Path.GetFullPath(rootPath);

        if (!Directory.Exists(RootPath)) {
            throw new DirectoryNotFoundException($"Root path does not exist: {RootPath}");
        }

        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Lists all items (files and directories) in the specified path
    /// </summary>
    /// <param name="path">The relative path to list items from</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A collection of sync items representing files and directories</returns>
    public async Task<IEnumerable<SyncItem>> ListItemsAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);
        var items = new List<SyncItem>();

        if (!Directory.Exists(fullPath)) {
            return items;
        }

        // Get directories
        foreach (var dir in Directory.EnumerateDirectories(fullPath)) {
            cancellationToken.ThrowIfCancellationRequested();

            var dirInfo = new DirectoryInfo(dir);
            items.Add(new SyncItem {
                Path = GetRelativePath(dir),
                IsDirectory = true,
                IsSymlink = dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint),
                LastModified = dirInfo.LastWriteTimeUtc,
                Size = 0
            });
        }

        // Get files
        var isUnix = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
        foreach (var file in Directory.EnumerateFiles(fullPath)) {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(file);
            var item = new SyncItem {
                Path = GetRelativePath(file),
                IsDirectory = false,
                IsSymlink = fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint),
                LastModified = fileInfo.LastWriteTimeUtc,
                Size = fileInfo.Length
            };

            if (isUnix) {
                item.Permissions = Convert.ToString((int)fileInfo.UnixFileMode, 8);
            }

            items.Add(item);
        }

        return await Task.FromResult(items).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets metadata for a specific item (file or directory)
    /// </summary>
    /// <param name="path">The relative path to the item</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>The sync item if it exists, null otherwise</returns>
    public async Task<SyncItem?> GetItemAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);

        if (Directory.Exists(fullPath)) {
            var dirInfo = new DirectoryInfo(fullPath);
            return await Task.FromResult(new SyncItem {
                Path = path,
                IsDirectory = true,
                IsSymlink = dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint),
                LastModified = dirInfo.LastWriteTimeUtc,
                Size = 0
            }).ConfigureAwait(false);
        }

        if (File.Exists(fullPath)) {
            var fileInfo = new FileInfo(fullPath);
            return await Task.FromResult(new SyncItem {
                Path = path,
                IsDirectory = false,
                IsSymlink = fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint),
                LastModified = fileInfo.LastWriteTimeUtc,
                Size = fileInfo.Length
            }).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    /// Reads the contents of a file from the local filesystem
    /// </summary>
    /// <param name="path">The relative path to the file</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A stream containing the file contents</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist</exception>
    public async Task<Stream> ReadFileAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);

        if (!File.Exists(fullPath)) {
            throw new FileNotFoundException($"File not found: {path}");
        }

        return await Task.FromResult(File.OpenRead(fullPath)).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes content to a file on the local filesystem, creating parent directories as needed
    /// </summary>
    /// <param name="path">The relative path to the file</param>
    /// <param name="content">The stream containing the file content to write</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    public async Task WriteFileAsync(string path, Stream content, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
            Directory.CreateDirectory(directory);
        }

        using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a directory on the local filesystem, including all parent directories if needed
    /// </summary>
    /// <param name="path">The relative path to the directory to create</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);
        Directory.CreateDirectory(fullPath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Deletes a file or directory from the local filesystem
    /// </summary>
    /// <param name="path">The relative path to the file or directory to delete</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <remarks>
    /// If the path is a directory, it will be deleted recursively along with all its contents
    /// </remarks>
    public Task DeleteAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);

        if (Directory.Exists(fullPath)) {
            Directory.Delete(fullPath, recursive: true);
        } else if (File.Exists(fullPath)) {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Moves or renames a file or directory on the local filesystem
    /// </summary>
    /// <param name="sourcePath">The relative path to the source file or directory</param>
    /// <param name="targetPath">The relative path to the target location</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <exception cref="FileNotFoundException">Thrown when the source does not exist</exception>
    public Task MoveAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default) {
        var sourceFullPath = GetFullPath(sourcePath);
        var targetFullPath = GetFullPath(targetPath);

        var targetDirectory = Path.GetDirectoryName(targetFullPath);
        if (!string.IsNullOrEmpty(targetDirectory) && !Directory.Exists(targetDirectory)) {
            Directory.CreateDirectory(targetDirectory);
        }

        if (Directory.Exists(sourceFullPath)) {
            Directory.Move(sourceFullPath, targetFullPath);
        } else if (File.Exists(sourceFullPath)) {
            File.Move(sourceFullPath, targetFullPath, overwrite: true);
        } else {
            throw new FileNotFoundException($"Source not found: {sourcePath}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks whether a file or directory exists on the local filesystem
    /// </summary>
    /// <param name="path">The relative path to check</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>True if the file or directory exists, false otherwise</returns>
    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);
        return await Task.FromResult(Directory.Exists(fullPath) || File.Exists(fullPath)).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets storage space information for the drive containing the root path
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Storage information including total and used space</returns>
    public async Task<StorageInfo> GetStorageInfoAsync(CancellationToken cancellationToken = default) {
        var driveInfo = new DriveInfo(Path.GetPathRoot(RootPath)!);

        return await Task.FromResult(new StorageInfo {
            TotalSpace = driveInfo.TotalSize,
            UsedSpace = driveInfo.TotalSize - driveInfo.AvailableFreeSpace
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the SHA256 hash of a file on the local filesystem
    /// </summary>
    /// <param name="path">The relative path to the file</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>Base64-encoded SHA256 hash of the file contents</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist</exception>
    public async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);

        if (!File.Exists(fullPath)) {
            throw new FileNotFoundException($"File not found: {path}");
        }

        using var stream = File.OpenRead(fullPath);
        using var sha256 = SHA256.Create();

        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Tests whether the local root directory is accessible
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>True if the root directory exists and is accessible</returns>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) {
        return await Task.FromResult(Directory.Exists(RootPath)).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the last modified time for a file or directory on the local filesystem
    /// </summary>
    public Task SetLastModifiedAsync(string path, DateTime lastModified, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);

        if (File.Exists(fullPath)) {
            File.SetLastWriteTimeUtc(fullPath, lastModified);
        } else if (Directory.Exists(fullPath)) {
            Directory.SetLastWriteTimeUtc(fullPath, lastModified);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets file permissions on the local filesystem (Unix/macOS only)
    /// </summary>
    public Task SetPermissionsAsync(string path, string permissions, CancellationToken cancellationToken = default) {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) {
            return Task.CompletedTask;
        }

        var fullPath = GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath)) {
            return Task.CompletedTask;
        }

        // Parse permissions string - support both "rwxr-xr-x" and numeric "755" formats
        if (TryParseUnixFileMode(permissions, out var mode)) {
            File.SetUnixFileMode(fullPath, mode);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Parses a permission string into a UnixFileMode value.
    /// Supports numeric format (e.g., "755") and symbolic format (e.g., "-rwxr-xr-x").
    /// </summary>
    private static bool TryParseUnixFileMode(string permissions, out UnixFileMode mode) {
        mode = UnixFileMode.None;
        if (string.IsNullOrWhiteSpace(permissions)) {
            return false;
        }

        // Try numeric format (e.g., "755") - validate it's all digits, then parse as octal
        if (permissions.Length >= 3 && permissions.Length <= 4 && permissions.All(char.IsDigit)) {
            mode = (UnixFileMode)Convert.ToInt32(permissions, 8);
            return true;
        }

        // Try symbolic format (e.g., "-rwxr-xr-x" or "drwxr-xr-x")
        var perm = permissions;
        if (perm.Length == 10) {
            perm = perm.Substring(1); // Strip type character (d, l, -)
        }

        if (perm.Length != 9) {
            return false;
        }

        if (perm[0] == 'r')
            mode |= UnixFileMode.UserRead;
        if (perm[1] == 'w')
            mode |= UnixFileMode.UserWrite;
        if (perm[2] == 'x')
            mode |= UnixFileMode.UserExecute;
        if (perm[3] == 'r')
            mode |= UnixFileMode.GroupRead;
        if (perm[4] == 'w')
            mode |= UnixFileMode.GroupWrite;
        if (perm[5] == 'x')
            mode |= UnixFileMode.GroupExecute;
        if (perm[6] == 'r')
            mode |= UnixFileMode.OtherRead;
        if (perm[7] == 'w')
            mode |= UnixFileMode.OtherWrite;
        if (perm[8] == 'x')
            mode |= UnixFileMode.OtherExecute;

        return true;
    }

    private string GetFullPath(string relativePath) {
        if (string.IsNullOrEmpty(relativePath) || relativePath == "/") {
            return RootPath;
        }

        // Normalize path separators and remove leading slash
        relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

        var fullPath = Path.Combine(RootPath, relativePath);

        // Ensure the path is within the root directory (security check)
        var normalizedFullPath = Path.GetFullPath(fullPath);
        var normalizedRoot = Path.GetFullPath(RootPath);

        if (!normalizedFullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)) {
            throw new UnauthorizedAccessException($"Path is outside root directory: {relativePath}");
        }

        return normalizedFullPath;
    }

    private string GetRelativePath(string fullPath) {
        var relativePath = Path.GetRelativePath(RootPath, fullPath);
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

}
