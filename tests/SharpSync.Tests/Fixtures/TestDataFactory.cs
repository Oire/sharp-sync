using Oire.SharpSync.Core;
using Oire.SharpSync.Storage;
using Oire.SharpSync.Database;
using Oire.SharpSync.Sync;

namespace Oire.SharpSync.Tests.Fixtures;

public static class TestDataFactory {
    public static SyncItem CreateSyncItem(
        string? name = null,
        string? path = null,
        bool? isDirectory = null,
        long? size = null,
        DateTime? lastModified = null,
        string? hash = null) {
        return new SyncItem {
            Path = path ?? $"{TestConstants.TestLocalPath}/{name ?? TestConstants.TestFileName}",
            IsDirectory = isDirectory ?? false,
            Size = size ?? TestConstants.TestFileSize,
            LastModified = lastModified ?? DateTime.UtcNow,
            Hash = hash ?? "test-hash-12345"
        };
    }

    public static SyncOptions CreateSyncOptions(
        ConflictResolution? conflictResolution = null,
        bool? dryRun = null,
        bool? deleteExtraneous = null,
        bool? updateExisting = null) {
        return new SyncOptions {
            ConflictResolution = conflictResolution ?? ConflictResolution.Ask,
            DryRun = dryRun ?? false,
            DeleteExtraneous = deleteExtraneous ?? false,
            UpdateExisting = updateExisting ?? true
        };
    }

    public static SyncState CreateSyncState(
        string? path = null,
        bool? isDirectory = null,
        long? localSize = null,
        DateTime? localModified = null,
        string? localHash = null,
        string? remoteHash = null,
        SyncStatus? status = null) {
        return new SyncState {
            Path = path ?? $"{TestConstants.TestLocalPath}/{TestConstants.TestFileName}",
            IsDirectory = isDirectory ?? false,
            LocalSize = localSize ?? TestConstants.TestFileSize,
            LocalModified = localModified ?? DateTime.UtcNow,
            LocalHash = localHash ?? "local-hash-12345",
            RemoteHash = remoteHash ?? "remote-hash-12345",
            Status = status ?? SyncStatus.Synced
        };
    }

    public static ConflictAnalysis CreateConflictAnalysis(
        string? filePath = null,
        ConflictType? conflictType = null,
        SyncItem? localItem = null,
        SyncItem? remoteItem = null) {
        return new ConflictAnalysis {
            FilePath = filePath ?? $"{TestConstants.TestLocalPath}/{TestConstants.TestFileName}",
            ConflictType = conflictType ?? ConflictType.BothModified,
            LocalItem = localItem ?? CreateSyncItem(),
            RemoteItem = remoteItem ?? CreateSyncItem(lastModified: DateTime.UtcNow.AddMinutes(5)),
            RecommendedResolution = ConflictResolution.UseRemote,
            LocalSize = localItem?.Size ?? TestConstants.TestFileSize,
            RemoteSize = remoteItem?.Size ?? TestConstants.TestFileSize,
            LocalModified = localItem?.LastModified ?? DateTime.UtcNow,
            RemoteModified = remoteItem?.LastModified ?? DateTime.UtcNow.AddMinutes(5)
        };
    }

    public static SyncProgress CreateSyncProgress(
        int? totalItems = null,
        int? processedItems = null,
        string? currentItem = null,
        bool? isCancelled = null) {
        return new SyncProgress {
            TotalItems = totalItems ?? 100,
            ProcessedItems = processedItems ?? 50,
            CurrentItem = currentItem ?? TestConstants.TestFileName,
            IsCancelled = isCancelled ?? false
        };
    }

    public static SyncResult CreateSyncResult(
        bool? success = null,
        long? filesSynchronized = null,
        long? filesSkipped = null,
        long? filesConflicted = null) {
        return new SyncResult {
            Success = success ?? true,
            FilesSynchronized = filesSynchronized ?? 10,
            FilesSkipped = filesSkipped ?? 0,
            FilesConflicted = filesConflicted ?? 0,
            FilesDeleted = 0,
            ElapsedTime = TimeSpan.FromMinutes(5),
            Error = null
        };
    }
}
