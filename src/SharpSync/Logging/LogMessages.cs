using Microsoft.Extensions.Logging;

namespace Oire.SharpSync.Logging;

/// <summary>
/// High-performance log messages using source generators.
/// </summary>
internal static partial class LogMessages {
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Error scanning directory {DirectoryPath}")]
    public static partial void DirectoryScanError(this ILogger logger, Exception ex, string directoryPath);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Error processing {FilePath}")]
    public static partial void ProcessingError(this ILogger logger, Exception ex, string filePath);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Error processing large file {FilePath}")]
    public static partial void LargeFileProcessingError(this ILogger logger, Exception ex, string filePath);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Error resolving conflict for {FilePath}")]
    public static partial void ConflictResolutionError(this ILogger logger, Exception ex, string filePath);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Error deleting {FilePath}")]
    public static partial void DeletionError(this ILogger logger, Exception ex, string filePath);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Warning,
        Message = "Virtual file callback failed for {FilePath}")]
    public static partial void VirtualFileCallbackError(this ILogger logger, Exception ex, string filePath);
}
