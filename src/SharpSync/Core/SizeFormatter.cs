namespace Oire.SharpSync.Core;

/// <summary>
/// Formats byte sizes into human-readable strings
/// </summary>
public static class SizeFormatter {
    private static readonly string[] Suffixes = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// Formats a byte count into a human-readable string (e.g., "2.5 MB")
    /// </summary>
    /// <param name="bytes">The number of bytes to format</param>
    /// <returns>A formatted string with appropriate unit suffix</returns>
    public static string Format(long bytes) {
        if (bytes == 0) {
            return "0 B";
        }

        if (bytes < 1024) {
            return $"{bytes} B";
        }

        var size = (double)bytes;
        var suffixIndex = 0;

        while (size >= 1024 && suffixIndex < Suffixes.Length - 1) {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:F1} {Suffixes[suffixIndex]}";
    }
}
