using System.Security.Cryptography;
using Oire.SharpSync.Core;

namespace Oire.SharpSync.Storage;

/// <summary>
/// Local filesystem storage implementation
/// </summary>
public class LocalFileStorage: ISyncStorage {
    private readonly string _rootPath;

    public StorageType StorageType => StorageType.Local;
    public string RootPath => _rootPath;

    public LocalFileStorage(string rootPath) {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path cannot be empty", nameof(rootPath));

        _rootPath = Path.GetFullPath(rootPath);

        if (!Directory.Exists(_rootPath))
            throw new DirectoryNotFoundException($"Root path does not exist: {_rootPath}");
    }

    public async Task<IEnumerable<SyncItem>> ListItemsAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);
        var items = new List<SyncItem>();

        if (!Directory.Exists(fullPath))
            return items;

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

    public async Task<Stream> ReadFileAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {path}");

        return await Task.FromResult(File.OpenRead(fullPath));
    }

    public async Task WriteFileAsync(string path, Stream content, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken);
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);
        Directory.CreateDirectory(fullPath);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);

        if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, recursive: true);
        else if (File.Exists(fullPath))
            File.Delete(fullPath);

        await Task.CompletedTask;
    }

    public async Task MoveAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default) {
        var sourceFullPath = GetFullPath(sourcePath);
        var targetFullPath = GetFullPath(targetPath);

        var targetDirectory = Path.GetDirectoryName(targetFullPath);
        if (!string.IsNullOrEmpty(targetDirectory) && !Directory.Exists(targetDirectory))
            Directory.CreateDirectory(targetDirectory);

        if (Directory.Exists(sourceFullPath))
            Directory.Move(sourceFullPath, targetFullPath);
        else if (File.Exists(sourceFullPath))
            File.Move(sourceFullPath, targetFullPath, overwrite: true);
        else
            throw new FileNotFoundException($"Source not found: {sourcePath}");

        await Task.CompletedTask;
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);
        return await Task.FromResult(Directory.Exists(fullPath) || File.Exists(fullPath));
    }

    public async Task<StorageInfo> GetStorageInfoAsync(CancellationToken cancellationToken = default) {
        var driveInfo = new DriveInfo(Path.GetPathRoot(_rootPath)!);

        return await Task.FromResult(new StorageInfo {
            TotalSpace = driveInfo.TotalSize,
            UsedSpace = driveInfo.TotalSize - driveInfo.AvailableFreeSpace
        });
    }

    public async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken = default) {
        var fullPath = GetFullPath(path);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {path}");

        using var stream = File.OpenRead(fullPath);
        using var sha256 = SHA256.Create();

        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToBase64String(hashBytes);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) {
        return await Task.FromResult(Directory.Exists(_rootPath));
    }

    private string GetFullPath(string relativePath) {
        if (string.IsNullOrEmpty(relativePath) || relativePath == "/")
            return _rootPath;

        // Normalize path separators and remove leading slash
        relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

        var fullPath = Path.Combine(_rootPath, relativePath);

        // Ensure the path is within the root directory (security check)
        var normalizedFullPath = Path.GetFullPath(fullPath);
        var normalizedRoot = Path.GetFullPath(_rootPath);

        if (!normalizedFullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Path is outside root directory: {relativePath}");

        return normalizedFullPath;
    }

    private string GetRelativePath(string fullPath) {
        var relativePath = Path.GetRelativePath(_rootPath, fullPath);
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
