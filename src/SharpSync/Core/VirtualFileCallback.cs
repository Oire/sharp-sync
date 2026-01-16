namespace Oire.SharpSync.Core;

/// <summary>
/// Delegate for handling virtual file placeholder creation after a file is downloaded.
/// </summary>
/// <param name="relativePath">The relative path of the downloaded file.</param>
/// <param name="localFullPath">The full local filesystem path where the file was written.</param>
/// <param name="fileMetadata">Metadata about the downloaded file including size, modified time, etc.</param>
/// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
/// <returns>A task that completes when the virtual file placeholder is created.</returns>
/// <remarks>
/// <para>
/// This callback is invoked by the sync engine after a file is successfully downloaded
/// to the local storage. Desktop clients can use this hook to integrate with platform-specific
/// virtual file systems like Windows Cloud Files API.
/// </para>
/// <para>
/// Example usage with Windows Cloud Files API:
/// <code>
/// options.VirtualFileCallback = async (path, fullPath, metadata, ct) =&gt; {
///     // Convert the downloaded file to a cloud files placeholder
///     await CloudFilesAPI.ConvertToPlaceholderAsync(fullPath, metadata.Size, ct);
/// };
/// </code>
/// </para>
/// <para>
/// If the callback throws an exception, the sync engine will log the error but continue
/// processing. The file will remain fully hydrated (not a placeholder) in that case.
/// </para>
/// </remarks>
public delegate Task VirtualFileCallbackDelegate(
    string relativePath,
    string localFullPath,
    SyncItem fileMetadata,
    CancellationToken cancellationToken
);
