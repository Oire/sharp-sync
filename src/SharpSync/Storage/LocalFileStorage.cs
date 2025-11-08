using System.Security.Cryptography;
using Oire.SharpSync.Core;

namespace Oire.SharpSync.Storage;

/// <summary>
/// Local filesystem storage implementation
/// </summary>
public class LocalFileStorage: ISyncStorage {
    /// <summary>
    /// Gets the storage type (always returns <see cref="Core.StorageType.Local"/>)
    /// </summary>
    public StorageType StorageType => StorageType.Local;

    /// <summary>
    /// Gets the root path on the local filesystem
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Creates a new local file storage instance
    /// </summary>
    /// <param name="rootPath">The root directory path for synchronization</param>
    /// <exception cref="ArgumentException">Thrown when rootPath is null or empty</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the root path does not exist</exception>
    public LocalFileStorage(string rootPath) {
        if (string.IsNullOrWhiteSpace(rootPath)) {
            throw new ArgumentException("Root path cannot be empty", nameof(rootPath));
        }

        RootPath = Path.GetFullPath(rootPath);

        if (!Directory.Exists(RootPath)) {
            throw new DirectoryNotFoundException($"Root path does not exist: {RootPath}");
        }
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
                LastModified = dirInfo.LastWriteTimeUtc,
                Size = 0
            });
        }

        // Get files
        foreach (var file in Directory.EnumerateFiles(fullPath)) {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(file);
            items.Add(new SyncItem {
                Path = GetRelativePath(file),
                IsDirectory = false,
                LastModified = fileInfo.LastWriteTimeUtc,
                Size = fileInfo.Length,
                MimeType = GetMimeType(file)
            });
        }

        return await Task.FromResult(items);
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
                LastModified = dirInfo.LastWriteTimeUtc,
                Size = 0
            });
        }

        if (File.Exists(fullPath)) {
            var fileInfo = new FileInfo(fullPath);
            return await Task.FromResult(new SyncItem {
                Path = path,
                IsDirectory = false,
                LastModified = fileInfo.LastWriteTimeUtc,
                Size = fileInfo.Length,
                MimeType = GetMimeType(fullPath)
            });
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

        return await Task.FromResult(File.OpenRead(fullPath));
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
        await content.CopyToAsync(fileStream, cancellationToken);
    }

    /// <summary>
    /// Creates a directory on the local filesystem, including all parent directories if needed
    /// </summary>
    /// <param name="path">The relative path to the directory to create</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);
        Directory.CreateDirectory(fullPath);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Deletes a file or directory from the local filesystem
    /// </summary>
    /// <param name="path">The relative path to the file or directory to delete</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <remarks>
    /// If the path is a directory, it will be deleted recursively along with all its contents
    /// </remarks>
    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);

        if (Directory.Exists(fullPath)) {
            Directory.Delete(fullPath, recursive: true);
        } else if (File.Exists(fullPath)) {
            File.Delete(fullPath);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Moves or renames a file or directory on the local filesystem
    /// </summary>
    /// <param name="sourcePath">The relative path to the source file or directory</param>
    /// <param name="targetPath">The relative path to the target location</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <exception cref="FileNotFoundException">Thrown when the source does not exist</exception>
    public async Task MoveAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default) {
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

        await Task.CompletedTask;
    }

    /// <summary>
    /// Checks whether a file or directory exists on the local filesystem
    /// </summary>
    /// <param name="path">The relative path to check</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>True if the file or directory exists, false otherwise</returns>
    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);
        return await Task.FromResult(Directory.Exists(fullPath) || File.Exists(fullPath));
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
        });
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

        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Tests whether the local root directory is accessible
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>True if the root directory exists and is accessible</returns>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) {
        return await Task.FromResult(Directory.Exists(RootPath));
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

    private static string GetMimeType(string filePath) {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch {
            ".txt" => "text/plain",
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".zip" => "application/zip",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".mp3" => "audio/mpeg",
            ".mp4" => "video/mp4",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
    }
}
