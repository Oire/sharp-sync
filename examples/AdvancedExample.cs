using Oire.SharpSync;
using System.Diagnostics;

namespace Oire.SharpSyncExamples;

/// <summary>
/// Example demonstrating advanced features of SharpSync
/// </summary>
class AdvancedExample
{
    static async Task Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: AdvancedExample <source> <target>");
            return;
        }

        var sourcePath = args[0];
        var targetPath = args[1];

        try
        {
            using var syncEngine = new SyncEngine();
            
            // Display library version
            Console.WriteLine($"Using CSync library version: {SyncEngine.LibraryVersion}");
            Console.WriteLine();

            // Configure advanced options
            var options = new SyncOptions
            {
                PreserveTimestamps = true,
                PreservePermissions = true,
                DeleteExtraneous = false,
                ConflictResolution = ConflictResolution.Ask,
                TimeoutSeconds = 300, // 5 minute timeout
                ExcludePatterns = new List<string>
                {
                    "*.tmp",
                    "*.log", 
                    ".DS_Store",
                    "Thumbs.db",
                    "~*",
                    "#*#",
                    ".git",
                    "node_modules"
                }
            };

            // Progress tracking
            var progressBar = new ConsoleProgressBar();
            syncEngine.ProgressChanged += (sender, progress) =>
            {
                progressBar.Update(progress.Percentage, progress.CurrentFileName);
            };

            // Conflict handling
            syncEngine.ConflictDetected += (sender, conflict) =>
            {
                Console.WriteLine($"\nConflict detected:");
                Console.WriteLine($"  Source: {conflict.SourcePath}");
                Console.WriteLine($"  Target: {conflict.TargetPath}");
                Console.WriteLine($"  Type: {conflict.ConflictType}");
                Console.Write("Resolution (S=Source, T=Target, K=Skip): ");
                
                var key = Console.ReadKey();
                Console.WriteLine();
                
                conflict.Resolution = key.Key switch
                {
                    ConsoleKey.S => ConflictResolution.UseSource,
                    ConsoleKey.T => ConflictResolution.UseTarget,
                    ConsoleKey.K => ConflictResolution.Skip,
                    _ => ConflictResolution.Skip
                };
            };

            // Start synchronization
            Console.WriteLine($"Synchronizing:");
            Console.WriteLine($"  From: {sourcePath}");
            Console.WriteLine($"  To: {targetPath}");
            Console.WriteLine($"  Timeout: {options.TimeoutSeconds} seconds");
            Console.WriteLine($"  Excluded: {string.Join(", ", options.ExcludePatterns)}");
            Console.WriteLine();

            var stopwatch = Stopwatch.StartNew();
            var result = await syncEngine.SynchronizeAsync(sourcePath, targetPath, options);
            stopwatch.Stop();

            // Display results
            Console.WriteLine();
            Console.WriteLine("Synchronization completed!");
            Console.WriteLine($"  Success: {result.Success}");
            Console.WriteLine($"  Files synchronized: {result.FilesSynchronized:N0}");
            Console.WriteLine($"  Files skipped: {result.FilesSkipped:N0}");
            Console.WriteLine($"  Files conflicted: {result.FilesConflicted:N0}");
            Console.WriteLine($"  Files deleted: {result.FilesDeleted:N0}");
            Console.WriteLine($"  Total processed: {result.TotalFilesProcessed:N0}");
            Console.WriteLine($"  Time elapsed: {result.ElapsedTime.TotalSeconds:F2} seconds");

            if (!result.Success && result.Error != null)
            {
                Console.WriteLine($"  Error: {result.Error.Message}");
            }
        }
        catch (TimeoutException ex)
        {
            Console.WriteLine($"ERROR: Synchronization timed out - {ex.Message}");
        }
        catch (SyncException ex)
        {
            Console.WriteLine($"ERROR: Synchronization failed - {ex.Message}");
            Console.WriteLine($"Error code: {ex.ErrorCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.GetType().Name} - {ex.Message}");
        }
    }
}

/// <summary>
/// Simple console progress bar implementation
/// </summary>
class ConsoleProgressBar
{
    private readonly object _lock = new object();
    private int _lastLength = 0;

    public void Update(double percentage, string currentFile)
    {
        lock (_lock)
        {
            var barLength = 50;
            var filled = (int)(percentage / 100.0 * barLength);
            var bar = new string('=', filled).PadRight(barLength, '-');

            var fileName = Path.GetFileName(currentFile);
            if (fileName.Length > 40)
                fileName = fileName.Substring(0, 37) + "...";

            var output = $"\r[{bar}] {percentage:F1}% - {fileName}";

            // Clear previous line if it was longer
            if (output.Length < _lastLength)
                output = output.PadRight(_lastLength);

            _lastLength = output.Length;
            Console.Write(output);
        }
    }
}
