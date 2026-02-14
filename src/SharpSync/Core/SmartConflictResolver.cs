using System.Collections.Frozen;

namespace Oire.SharpSync.Core;

/// <summary>
/// Enhanced conflict resolver that provides intelligent recommendations
/// UI-free but provides rich data for UI decision making
/// </summary>
public class SmartConflictResolver: IConflictResolver {
    /// <summary>
    /// Delegate for UI-driven conflict resolution.
    /// Desktop clients can implement this to show UI dialogs.
    /// </summary>
    public delegate Task<ConflictResolution> ConflictHandlerDelegate(ConflictAnalysis analysis, CancellationToken cancellationToken);

    private static readonly FrozenSet<string> BinaryExtensions = FrozenSet.ToFrozenSet<string>([
        ".exe", ".dll", ".bin", ".zip", ".7z", ".rar",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".webp",
        ".mp4", ".avi", ".mkv", ".mp3", ".wav", ".ogg", ".flac", ".mov", ".wmv", ".alac", ".wma",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".mo", ".epub",
    ], StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> TextExtensions = FrozenSet.ToFrozenSet<string>([
        ".txt", ".md", ".json", ".xml", ".yml", ".yaml", ".om", ".toml", ".m3u", ".m3u8", ".fb2",
        ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".rb", ".go", ".rs", ".swift", ".kt", ".dart", ".lua", ".sh", ".bat", ".ps1", ".sql", ".zig", ".d", ".lr", ".po",
        ".css", ".scss", ".less", ".html", ".htm", ".php",
        ".ini", ".cfg", ".conf", ".log"
    ], StringComparer.OrdinalIgnoreCase);

    private readonly ConflictHandlerDelegate? _conflictHandler;
    private readonly ConflictResolution _defaultResolution;

    /// <summary>
    /// Creates a smart conflict resolver
    /// </summary>
    /// <param name="conflictHandler">Optional handler for UI interaction</param>
    /// <param name="defaultResolution">Default resolution when no handler provided</param>
    public SmartConflictResolver(ConflictHandlerDelegate? conflictHandler = null, ConflictResolution defaultResolution = ConflictResolution.Skip) {
        _conflictHandler = conflictHandler;
        _defaultResolution = defaultResolution;
    }

    /// <summary>
    /// Resolves conflicts with intelligent analysis
    /// </summary>
    public async Task<ConflictResolution> ResolveConflictAsync(FileConflictEventArgs conflict, CancellationToken cancellationToken = default) {
        // Analyze the conflict to provide rich information
        var analysis = await AnalyzeConflictAsync(conflict, cancellationToken).ConfigureAwait(false);

        // If we have a UI handler, let it decide
        if (_conflictHandler is not null) {
            return await _conflictHandler(analysis, cancellationToken).ConfigureAwait(false);
        }

        // Otherwise, use intelligent automatic resolution
        return ResolveAutomatically(analysis);
    }

    /// <summary>
    /// Analyzes a conflict to provide rich information for decision making
    /// </summary>
    private static Task<ConflictAnalysis> AnalyzeConflictAsync(FileConflictEventArgs conflict, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        // Collect analysis data
        long localSize = 0;
        long remoteSize = 0;
        long sizeDifference = 0;
        DateTime? localModified = null;
        DateTime? remoteModified = null;
        var timeDifference = TimeSpan.Zero;
        string? newerVersion = null;
        var recommendedResolution = ConflictResolution.Ask;

        // Analyze file sizes
        if (conflict.LocalItem is not null && conflict.RemoteItem is not null) {
            localSize = conflict.LocalItem.Size;
            remoteSize = conflict.RemoteItem.Size;
            sizeDifference = Math.Abs(conflict.RemoteItem.Size - conflict.LocalItem.Size);
        }

        // Analyze timestamps
        if (conflict.LocalItem?.LastModified is not null && conflict.RemoteItem?.LastModified is not null) {
            localModified = conflict.LocalItem.LastModified;
            remoteModified = conflict.RemoteItem.LastModified;
            timeDifference = (conflict.RemoteItem.LastModified - conflict.LocalItem.LastModified).Duration();

            // Determine which is newer
            if (conflict.RemoteItem.LastModified > conflict.LocalItem.LastModified) {
                newerVersion = "Remote";
                recommendedResolution = ConflictResolution.UseRemote;
            } else if (conflict.LocalItem.LastModified > conflict.RemoteItem.LastModified) {
                newerVersion = "Local";
                recommendedResolution = ConflictResolution.UseLocal;
            }
        }

        // Analyze file types
        var isLikelyBinary = IsLikelyBinaryFile(conflict.Path);
        var isLikelyTextFile = IsLikelyTextFile(conflict.Path);

        // Special handling for different conflict types
        switch (conflict.ConflictType) {
            case ConflictType.DeletedLocallyModifiedRemotely:
                recommendedResolution = ConflictResolution.UseRemote;
                break;

            case ConflictType.ModifiedLocallyDeletedRemotely:
                recommendedResolution = ConflictResolution.UseLocal;
                break;

            case ConflictType.TypeConflict:
                recommendedResolution = ConflictResolution.Ask;
                break;
        }

        // Create immutable analysis record
        return Task.FromResult(new ConflictAnalysis {
            FilePath = conflict.Path,
            ConflictType = conflict.ConflictType,
            LocalItem = conflict.LocalItem,
            RemoteItem = conflict.RemoteItem,
            RecommendedResolution = recommendedResolution,
            LocalSize = localSize,
            RemoteSize = remoteSize,
            SizeDifference = sizeDifference,
            LocalModified = localModified,
            RemoteModified = remoteModified,
            TimeDifference = timeDifference,
            NewerVersion = newerVersion,
            IsLikelyBinary = isLikelyBinary,
            IsLikelyTextFile = isLikelyTextFile
        });
    }

    /// <summary>
    /// Provides automatic resolution based on analysis
    /// </summary>
    private ConflictResolution ResolveAutomatically(ConflictAnalysis analysis) {
        // Use recommended resolution if available
        if (analysis.RecommendedResolution != ConflictResolution.Ask) {
            return analysis.RecommendedResolution;
        }

        // Fall back to default
        return _defaultResolution;
    }

    private static bool IsLikelyBinaryFile(string path) =>
        BinaryExtensions.Contains(Path.GetExtension(path));

    private static bool IsLikelyTextFile(string path) =>
        TextExtensions.Contains(Path.GetExtension(path));
}
