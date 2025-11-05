namespace Oire.SharpSync.Core;

/// <summary>
/// Enhanced conflict resolver that provides intelligent recommendations
/// UI-free but provides rich data for UI decision making
/// </summary>
public class SmartConflictResolver: IConflictResolver {
    /// <summary>
    /// Delegate for UI-driven conflict resolution
    /// Nimbus can implement this to show dialogs
    /// </summary>
    public delegate Task<ConflictResolution> ConflictHandlerDelegate(ConflictAnalysis analysis, CancellationToken cancellationToken);

    private readonly ConflictHandlerDelegate? _conflictHandler;
    private readonly ConflictResolution _defaultResolution;

    /// <summary>
    /// Creates a smart conflict resolver
    /// </summary>
    /// <param name="conflictHandler">Optional handler for UI interaction</param>
    /// <param name="defaultResolution">Default resolution when no handler provided</param>
    public SmartConflictResolver(ConflictHandlerDelegate? conflictHandler = null, ConflictResolution defaultResolution = ConflictResolution.Ask) {
        _conflictHandler = conflictHandler;
        _defaultResolution = defaultResolution;
    }

    /// <summary>
    /// Resolves conflicts with intelligent analysis
    /// </summary>
    public async Task<ConflictResolution> ResolveConflictAsync(FileConflictEventArgs conflict, CancellationToken cancellationToken = default) {
        // Analyze the conflict to provide rich information
        var analysis = await AnalyzeConflictAsync(conflict, cancellationToken);

        // If we have a UI handler, let it decide
        if (_conflictHandler is not null) {
            return await _conflictHandler(analysis, cancellationToken);
        }

        // Otherwise, use intelligent automatic resolution
        return ResolveAutomatically(analysis);
    }

    /// <summary>
    /// Analyzes a conflict to provide rich information for decision making
    /// </summary>
    private static async Task<ConflictAnalysis> AnalyzeConflictAsync(FileConflictEventArgs conflict, CancellationToken cancellationToken) {
        // Collect analysis data
        long localSize = 0;
        long remoteSize = 0;
        long sizeDifference = 0;
        DateTime? localModified = null;
        DateTime? remoteModified = null;
        double timeDifference = 0;
        string? newerVersion = null;
        var recommendedResolution = ConflictResolution.Ask;
        var reasoning = string.Empty;

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
            timeDifference = Math.Abs((conflict.RemoteItem.LastModified - conflict.LocalItem.LastModified).TotalSeconds);

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
                reasoning = "File was deleted locally but modified remotely. Remote version is likely more current.";
                break;

            case ConflictType.ModifiedLocallyDeletedRemotely:
                recommendedResolution = ConflictResolution.UseLocal;
                reasoning = "File was modified locally but deleted remotely. Local changes may be important.";
                break;

            case ConflictType.TypeConflict:
                recommendedResolution = ConflictResolution.Ask;
                reasoning = "File/directory type conflict requires manual resolution.";
                break;

            case ConflictType.BothModified:
                // Already handled by timestamp analysis above
                if (string.IsNullOrEmpty(reasoning)) {
                    reasoning = timeDifference < 60
                        ? "Files modified within 1 minute - likely simultaneous edits."
                        : $"Files have different modification times. Recommending {newerVersion?.ToLower()} version.";
                }
                break;
        }

        await Task.CompletedTask; // Make it truly async

        // Create immutable analysis record
        return new ConflictAnalysis {
            FilePath = conflict.Path,
            ConflictType = conflict.ConflictType,
            LocalItem = conflict.LocalItem,
            RemoteItem = conflict.RemoteItem,
            RecommendedResolution = recommendedResolution,
            Reasoning = reasoning,
            LocalSize = localSize,
            RemoteSize = remoteSize,
            SizeDifference = sizeDifference,
            LocalModified = localModified,
            RemoteModified = remoteModified,
            TimeDifference = timeDifference,
            NewerVersion = newerVersion,
            IsLikelyBinary = isLikelyBinary,
            IsLikelyTextFile = isLikelyTextFile
        };
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

    private static bool IsLikelyBinaryFile(string path) {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch {
            ".exe" or ".dll" or ".bin" or ".zip" or ".7z" or ".rar" or
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".ico" or
            ".mp4" or ".avi" or ".mkv" or ".mp3" or ".wav" or ".ogg" or
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" => true,
            _ => false
        };
    }

    private static bool IsLikelyTextFile(string path) {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch {
            ".txt" or ".md" or ".json" or ".xml" or ".yml" or ".yaml" or
            ".cs" or ".js" or ".ts" or ".py" or ".java" or ".cpp" or ".c" or ".h" or
            ".css" or ".scss" or ".less" or ".html" or ".htm" or ".php" or
            ".ini" or ".cfg" or ".conf" or ".log" => true,
            _ => false
        };
    }
}
