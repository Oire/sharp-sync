using Oire.SharpSync.Core;
using Oire.SharpSync.Database;
using Oire.SharpSync.Storage;
using Oire.SharpSync.Sync;

namespace SharpSync.Samples.Console;

public static class Program {
    public static async Task Main(string[] args) {
        System.Console.WriteLine("=== SharpSync Samples ===");
        System.Console.WriteLine();
        System.Console.WriteLine("1. Basic local-to-local sync (runs with temp directories)");
        System.Console.WriteLine("2. View all sync option examples (display only)");
        System.Console.WriteLine("3. OAuth2 Nextcloud sync (requires live server)");
        System.Console.WriteLine("4. Exit");
        System.Console.WriteLine();

        while (true) {
            System.Console.Write("Choose a sample [1-4]: ");
            var choice = System.Console.ReadLine()?.Trim();

            switch (choice) {
                case "1":
                    await RunBasicLocalSyncAsync();
                    break;
                case "2":
                    ShowSyncOptionsOverview();
                    break;
                case "3":
                    await OAuth2SyncExample.RunAsync();
                    break;
                case "4":
                    return;
                default:
                    System.Console.WriteLine("Invalid choice. Please enter 1-4.");
                    break;
            }

            System.Console.WriteLine();
        }
    }

    /// <summary>
    /// Runs a real local-to-local sync using temporary directories.
    /// </summary>
    private static async Task RunBasicLocalSyncAsync() {
        // Create temp directories for the demo
        var tempBase = Path.Combine(Path.GetTempPath(), "SharpSync-Sample");
        var localPath = Path.Combine(tempBase, "local");
        var remotePath = Path.Combine(tempBase, "remote");
        var dbPath = Path.Combine(tempBase, "sync.db");

        Directory.CreateDirectory(localPath);
        Directory.CreateDirectory(remotePath);

        try {
            // Create some sample files in "local"
            await File.WriteAllTextAsync(
                Path.Combine(localPath, "hello.txt"),
                "Hello from SharpSync!");
            await File.WriteAllTextAsync(
                Path.Combine(localPath, "notes.md"),
                "# Notes\n\nThis file was synced by SharpSync.");

            // Create a sample file in "remote"
            await File.WriteAllTextAsync(
                Path.Combine(remotePath, "remote-file.txt"),
                "This file exists only on the remote side.");

            System.Console.WriteLine($"Local dir:  {localPath}");
            System.Console.WriteLine($"Remote dir: {remotePath}");
            System.Console.WriteLine();

            // Set up storage, database, and engine
            var localStorage = new LocalFileStorage(localPath);
            var remoteStorage = new LocalFileStorage(remotePath);
            var database = new SqliteSyncDatabase(dbPath);
            await database.InitializeAsync();

            var filter = new SyncFilter();
            var resolver = new DefaultConflictResolver(ConflictResolution.UseRemote);

            using var engine = new SyncEngine(
                localStorage, remoteStorage, database, resolver, filter);

            engine.ProgressChanged += (_, e) =>
                System.Console.WriteLine(
                    $"  [{e.Progress.Percentage:F0}%] {e.Operation}: {e.Progress.CurrentItem}");

            // Preview the sync plan
            var plan = await engine.GetSyncPlanAsync();
            System.Console.WriteLine(
                $"Sync plan: {plan.TotalActions} actions ({plan.UploadCount} uploads, {plan.DownloadCount} downloads, {plan.ConflictCount} conflicts)");
            System.Console.WriteLine(
                $"  Uploads: {plan.Uploads.Count}, Downloads: {plan.Downloads.Count}");
            System.Console.WriteLine();

            // Run sync
            var result = await engine.SynchronizeAsync();
            System.Console.WriteLine(
                $"Sync completed: {result.FilesSynchronized} files synchronized");

            // Show resulting files
            System.Console.WriteLine();
            System.Console.WriteLine("Files in local dir after sync:");
            foreach (var file in Directory.GetFiles(localPath)) {
                System.Console.WriteLine($"  {Path.GetFileName(file)}");
            }

            System.Console.WriteLine("Files in remote dir after sync:");
            foreach (var file in Directory.GetFiles(remotePath)) {
                System.Console.WriteLine($"  {Path.GetFileName(file)}");
            }
        } finally {
            // Cleanup
            try {
                Directory.Delete(tempBase, recursive: true);
                System.Console.WriteLine();
                System.Console.WriteLine("Temp files cleaned up.");
            } catch {
                System.Console.WriteLine($"Note: Could not clean up {tempBase}");
            }
        }
    }

    /// <summary>
    /// Displays an overview of the available SyncOptions.
    /// </summary>
    private static void ShowSyncOptionsOverview() {
        System.Console.WriteLine();
        System.Console.WriteLine("=== SyncOptions Overview ===");
        System.Console.WriteLine();
        System.Console.WriteLine("SharpSync supports these options (combine as needed):");
        System.Console.WriteLine();
        System.Console.WriteLine(
            "  ChecksumOnly        Detect changes by file hash instead of timestamps");
        System.Console.WriteLine(
            "  SizeOnly            Detect changes by file size only (fastest)");
        System.Console.WriteLine(
            "  PreserveTimestamps  Copy modification times to the target");
        System.Console.WriteLine(
            "  PreservePermissions Copy Unix permissions (Local/SFTP only)");
        System.Console.WriteLine(
            "  FollowSymlinks      Follow symlink directories during sync");
        System.Console.WriteLine(
            "  ExcludePatterns     Additional exclude globs for this sync run");
        System.Console.WriteLine(
            "  TimeoutSeconds      Cancel sync after N seconds");
        System.Console.WriteLine(
            "  UpdateExisting      Set to false to sync only new files");
        System.Console.WriteLine(
            "  MaxBytesPerSecond   Bandwidth throttling (bytes/sec)");
        System.Console.WriteLine(
            "  ConflictResolution  Override conflict strategy per sync");
        System.Console.WriteLine(
            "  Verbose             Emit detailed Debug-level log messages");
        System.Console.WriteLine();
        System.Console.WriteLine(
            "See BasicSyncExample.cs for code samples of each option.");
    }
}
