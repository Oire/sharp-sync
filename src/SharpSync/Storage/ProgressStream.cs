namespace Oire.SharpSync.Storage;

/// <summary>
/// Stream wrapper that reports progress during read operations
/// </summary>
internal sealed class ProgressStream: Stream {
    private readonly Stream _innerStream;
    private readonly long _totalLength;
    private readonly Action<long, long> _progressCallback;
    private long _bytesRead;
    private readonly object _lock = new();

    public ProgressStream(Stream innerStream, long totalLength, Action<long, long> progressCallback) {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _totalLength = totalLength;
        _progressCallback = progressCallback ?? throw new ArgumentNullException(nameof(progressCallback));
        _bytesRead = 0;

        // Report initial progress
        _progressCallback(0, _totalLength);
    }

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => _innerStream.CanSeek;
    public override bool CanWrite => _innerStream.CanWrite;
    public override long Length => _innerStream.Length;
    public override long Position {
        get => _innerStream.Position;
        set => _innerStream.Position = value;
    }

    public override void Flush() => _innerStream.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _innerStream.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) {
        var bytesRead = _innerStream.Read(buffer, offset, count);
        UpdateProgress(bytesRead);
        return bytesRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
        var bytesRead = await _innerStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        UpdateProgress(bytesRead);
        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
        var bytesRead = await _innerStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        UpdateProgress(bytesRead);
        return bytesRead;
    }

    private void UpdateProgress(int bytesJustRead) {
        if (bytesJustRead > 0) {
            lock (_lock) {
                _bytesRead += bytesJustRead;
                _progressCallback(_bytesRead, _totalLength);
            }
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
    public override void SetLength(long value) => _innerStream.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _innerStream.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => _innerStream.WriteAsync(buffer, cancellationToken);

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _innerStream?.Dispose();
        }
        base.Dispose(disposing);
    }
}
