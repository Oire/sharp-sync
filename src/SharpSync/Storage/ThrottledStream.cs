using System.Diagnostics;

namespace Oire.SharpSync.Storage;

/// <summary>
/// Stream wrapper that throttles read and write operations to limit bandwidth usage.
/// Uses a token bucket algorithm for smooth rate limiting.
/// </summary>
internal sealed class ThrottledStream: Stream {
    private readonly Stream _innerStream;
    private readonly long _maxBytesPerSecond;
    private readonly Stopwatch _stopwatch;
    private long _totalBytesTransferred;
    private readonly object _lock = new();

    /// <summary>
    /// Creates a new ThrottledStream wrapping the specified stream.
    /// </summary>
    /// <param name="innerStream">The stream to wrap.</param>
    /// <param name="maxBytesPerSecond">Maximum bytes per second (must be positive).</param>
    /// <exception cref="ArgumentNullException">Thrown when innerStream is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when maxBytesPerSecond is not positive.</exception>
    public ThrottledStream(Stream innerStream, long maxBytesPerSecond) {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));

        if (maxBytesPerSecond <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maxBytesPerSecond), "Max bytes per second must be positive.");
        }

        _maxBytesPerSecond = maxBytesPerSecond;
        _stopwatch = Stopwatch.StartNew();
        _totalBytesTransferred = 0;
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

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _innerStream.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) {
        ThrottleSync(count);
        var bytesRead = _innerStream.Read(buffer, offset, count);
        RecordBytesTransferred(bytesRead);
        return bytesRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
        await ThrottleAsync(count, cancellationToken);
        var bytesRead = await _innerStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        RecordBytesTransferred(bytesRead);
        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
        await ThrottleAsync(buffer.Length, cancellationToken);
        var bytesRead = await _innerStream.ReadAsync(buffer, cancellationToken);
        RecordBytesTransferred(bytesRead);
        return bytesRead;
    }

    public override void Write(byte[] buffer, int offset, int count) {
        ThrottleSync(count);
        _innerStream.Write(buffer, offset, count);
        RecordBytesTransferred(count);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
        await ThrottleAsync(count, cancellationToken);
        await _innerStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        RecordBytesTransferred(count);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
        await ThrottleAsync(buffer.Length, cancellationToken);
        await _innerStream.WriteAsync(buffer, cancellationToken);
        RecordBytesTransferred(buffer.Length);
    }

    public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);

    public override void SetLength(long value) => _innerStream.SetLength(value);

    /// <summary>
    /// Synchronously waits if the transfer rate would exceed the limit.
    /// </summary>
    private void ThrottleSync(int requestedBytes) {
        var delay = CalculateDelay(requestedBytes);
        if (delay > TimeSpan.Zero) {
            Thread.Sleep(delay);
        }
    }

    /// <summary>
    /// Asynchronously waits if the transfer rate would exceed the limit.
    /// </summary>
    private async Task ThrottleAsync(int requestedBytes, CancellationToken cancellationToken) {
        var delay = CalculateDelay(requestedBytes);
        if (delay > TimeSpan.Zero) {
            await Task.Delay(delay, cancellationToken);
        }
    }

    /// <summary>
    /// Calculates the delay needed to maintain the target transfer rate.
    /// </summary>
    private TimeSpan CalculateDelay(int requestedBytes) {
        lock (_lock) {
            var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
            if (elapsedSeconds <= 0) {
                return TimeSpan.Zero;
            }

            // Calculate the expected time for the bytes already transferred plus the new bytes
            var expectedBytes = _totalBytesTransferred + requestedBytes;
            var expectedTimeSeconds = (double)expectedBytes / _maxBytesPerSecond;

            // If we're ahead of schedule, delay
            if (expectedTimeSeconds > elapsedSeconds) {
                var delaySeconds = expectedTimeSeconds - elapsedSeconds;
                // Cap delay to a reasonable maximum to prevent extremely long waits
                delaySeconds = Math.Min(delaySeconds, 5.0);
                return TimeSpan.FromSeconds(delaySeconds);
            }

            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Records that bytes have been transferred.
    /// </summary>
    private void RecordBytesTransferred(int bytes) {
        if (bytes > 0) {
            lock (_lock) {
                _totalBytesTransferred += bytes;
            }
        }
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _innerStream?.Dispose();
        }
        base.Dispose(disposing);
    }
}
