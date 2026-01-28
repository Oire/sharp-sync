namespace Oire.SharpSync.Core;

/// <summary>
/// Event arguments for per-file transfer progress during sync operations.
/// </summary>
/// <remarks>
/// <para>
/// This event provides byte-level progress for individual file transfers,
/// allowing UI applications to display detailed progress bars for large files.
/// </para>
/// <para>
/// This complements <see cref="SyncProgressEventArgs"/> which reports item-level progress
/// (number of files processed). Use both events together for comprehensive progress reporting:
/// <list type="bullet">
/// <item><description><see cref="SyncProgressEventArgs"/>: Overall sync progress (X of Y files)</description></item>
/// <item><description><see cref="FileProgressEventArgs"/>: Current file progress (X of Y bytes)</description></item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// engine.FileProgressChanged += (sender, e) => {
///     // Update per-file progress bar
///     FileProgressBar.Value = e.PercentComplete;
///     FileProgressLabel.Text = $"{e.Path}: {e.BytesTransferred / 1024}KB / {e.TotalBytes / 1024}KB";
///     OperationLabel.Text = e.Operation == FileTransferOperation.Upload ? "Uploading" : "Downloading";
/// };
/// </code>
/// </example>
public class FileProgressEventArgs: EventArgs {
    /// <summary>
    /// Gets the relative path of the file being transferred.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the number of bytes transferred so far.
    /// </summary>
    public long BytesTransferred { get; }

    /// <summary>
    /// Gets the total number of bytes to transfer.
    /// </summary>
    public long TotalBytes { get; }

    /// <summary>
    /// Gets the type of transfer operation (upload or download).
    /// </summary>
    public FileTransferOperation Operation { get; }

    /// <summary>
    /// Gets the percentage complete (0-100).
    /// </summary>
    public int PercentComplete => TotalBytes > 0 ? (int)(BytesTransferred * 100 / TotalBytes) : 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileProgressEventArgs"/> class.
    /// </summary>
    /// <param name="path">The relative path of the file being transferred</param>
    /// <param name="bytesTransferred">The number of bytes transferred so far</param>
    /// <param name="totalBytes">The total number of bytes to transfer</param>
    /// <param name="operation">The type of transfer operation</param>
    public FileProgressEventArgs(string path, long bytesTransferred, long totalBytes, FileTransferOperation operation) {
        Path = path;
        BytesTransferred = bytesTransferred;
        TotalBytes = totalBytes;
        Operation = operation;
    }
}
