using Oire.SharpSync.Core;
using Oire.SharpSync.Storage;
using Oire.SharpSync.Database;
using Oire.SharpSync.Sync;

namespace Oire.SharpSyncExamples;

/// <summary>
/// Basic example showing how to sync between local folders or with WebDAV
/// </summary>
class BasicSyncExample
{
    static async Task Main(string[] args)
    {
        // Example 1: Local folder sync
        await LocalFolderSyncExample();
        
        // Example 2: WebDAV sync (uncomment to use)
        // await WebDavSyncExample();
    }

    static async Task LocalFolderSyncExample()
    {
        Console.WriteLine("=== Local Folder Sync Example ===\n");

        // Setup paths
        var sourcePath = @"C:\SyncTest\Source";
        var targetPath = @"C:\SyncTest\Target";
        var databasePath = @"C:\SyncTest\sync.db";

        // Create test directories
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        // Create storage instances
        var localStorage = new LocalFileStorage(sourcePath);
        var remoteStorage = new LocalFileStorage(targetPath); // Using local as "remote" for demo

        // Create database
        var database = new SqliteSyncDatabase(databasePath);
        await database.InitializeAsync();

        // Create filter with common exclusions
        var filter = SyncFilter.CreateDefault();
        filter.AddExclusionPattern("*.log");
        filter.AddExclusionPattern("temp/");

        // Create conflict resolver
        var conflictResolver = new DefaultConflictResolver(ConflictResolutionStrategy.PreferNewer);

        // Create sync engine
        using var syncEngine = new SyncEngine(localStorage, remoteStorage, database, filter, conflictResolver);

        // Subscribe to events
        syncEngine.ProgressChanged += (sender, e) =>
        {
            Console.WriteLine($"[{e.Progress.Percentage:F1}%] {e.Operation}: {e.CurrentItem}");
        };

        syncEngine.ConflictDetected += (sender, e) =>
        {
            Console.WriteLine($"Conflict detected: {e.Path}");
            Console.WriteLine($"  Local: {e.LocalItem?.LastModified} ({e.LocalItem?.Size} bytes)");
            Console.WriteLine($"  Remote: {e.RemoteItem?.LastModified} ({e.RemoteItem?.Size} bytes)");
            // The conflict resolver will handle it automatically
        };

        // Create sync options
        var options = new SyncOptions
        {
            DeleteExtraneous = false, // Don't delete files that exist only in target
            PreserveTimestamps = true,
            DryRun = false // Set to true to preview without making changes
        };

        try
        {
            // Perform sync
            Console.WriteLine($"Syncing from {sourcePath} to {targetPath}...\n");
            var result = await syncEngine.SynchronizeAsync(options);

            // Show results
            Console.WriteLine($"\nSync completed in {result.ElapsedTime.TotalSeconds:F2} seconds");
            Console.WriteLine($"Success: {result.Success}");
            Console.WriteLine($"Files synchronized: {result.FilesSynchronized}");
            Console.WriteLine($"Files skipped: {result.FilesSkipped}");
            Console.WriteLine($"Files conflicted: {result.FilesConflicted}");
            Console.WriteLine($"Files deleted: {result.FilesDeleted}");
            
            if (!result.Success && result.Error != null)
            {
                Console.WriteLine($"Error: {result.Error.Message}");
            }

            // Show statistics
            var stats = await syncEngine.GetStatsAsync();
            Console.WriteLine($"\nDatabase statistics:");
            Console.WriteLine($"Total items tracked: {stats.TotalItems}");
            Console.WriteLine($"Synced items: {stats.SyncedItems}");
            Console.WriteLine($"Pending items: {stats.PendingItems}");
            Console.WriteLine($"Conflicted items: {stats.ConflictedItems}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Sync failed: {ex.Message}");
        }
    }

    static async Task WebDavSyncExample()
    {
        Console.WriteLine("=== WebDAV Sync Example ===\n");

        // Setup
        var localPath = @"C:\NextcloudSync\Documents";
        var databasePath = @"C:\NextcloudSync\sync.db";
        
        // WebDAV configuration
        var webdavUrl = "https://cloud.example.com/remote.php/dav/files/username/";
        var username = "your-username";
        var password = "your-app-password"; // Use app-specific password

        Directory.CreateDirectory(localPath);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        // Create storage instances
        var localStorage = new LocalFileStorage(localPath);
        var remoteStorage = new WebDavStorage(webdavUrl, username, password, "Documents");

        // Test connection
        Console.WriteLine("Testing WebDAV connection...");
        if (!await remoteStorage.TestConnectionAsync())
        {
            Console.WriteLine("Failed to connect to WebDAV server!");
            return;
        }
        Console.WriteLine("Connected successfully!\n");

        // Create other components
        var database = new SqliteSyncDatabase(databasePath);
        await database.InitializeAsync();

        var filter = SyncFilter.CreateDefault();
        var conflictResolver = new DefaultConflictResolver(ConflictResolutionStrategy.PreferNewer);

        // Create sync engine
        using var syncEngine = new SyncEngine(localStorage, remoteStorage, database, filter, conflictResolver);

        // Subscribe to progress
        syncEngine.ProgressChanged += (sender, e) =>
        {
            var progress = e.Progress;
            Console.Write($"\r[{progress.Percentage:F1}%] {progress.CurrentFile}/{progress.TotalFiles} - {Path.GetFileName(e.CurrentItem ?? "")}     ");
        };

        // Sync options
        var options = new SyncOptions
        {
            DeleteExtraneous = false,
            PreserveTimestamps = true,
            TimeoutSeconds = 300 // 5 minute timeout
        };

        try
        {
            Console.WriteLine("Starting synchronization...\n");
            var result = await syncEngine.SynchronizeAsync(options);
            
            Console.WriteLine($"\n\nSync completed!");
            Console.WriteLine($"Files synchronized: {result.FilesSynchronized}");
            Console.WriteLine($"Time taken: {result.ElapsedTime.TotalSeconds:F2} seconds");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nSync failed: {ex.Message}");
        }
    }
}