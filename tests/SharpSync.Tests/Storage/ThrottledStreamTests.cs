using System.Diagnostics;
using System.Reflection;

namespace Oire.SharpSync.Tests.Storage;

public class ThrottledStreamTests {
    [Fact]
    public void Constructor_ValidParameters_InitializesCorrectly() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3, 4, 5]);

        // Act
        using var throttledStream = CreateThrottledStream(innerStream, 1_000_000);

        // Assert
        Assert.NotNull(throttledStream);
    }

    [Fact]
    public void Constructor_NullInnerStream_ThrowsArgumentNullException() {
        // Act & Assert - Reflection wraps in TargetInvocationException
        var exception = Assert.Throws<TargetInvocationException>(() =>
            CreateThrottledStream(null!, 1_000_000));
        Assert.IsType<ArgumentNullException>(exception.InnerException);
    }

    [Fact]
    public void Constructor_ZeroMaxBytesPerSecond_ThrowsArgumentOutOfRangeException() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3]);

        // Act & Assert - Reflection wraps in TargetInvocationException
        var exception = Assert.Throws<TargetInvocationException>(() =>
            CreateThrottledStream(innerStream, 0));
        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
    }

    [Fact]
    public void Constructor_NegativeMaxBytesPerSecond_ThrowsArgumentOutOfRangeException() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3]);

        // Act & Assert - Reflection wraps in TargetInvocationException
        var exception = Assert.Throws<TargetInvocationException>(() =>
            CreateThrottledStream(innerStream, -100));
        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
    }

    [Fact]
    public void Read_ReturnsCorrectData() {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var throttledStream = CreateThrottledStream(innerStream, 1_000_000);
        var buffer = new byte[5];

        // Act
        var bytesRead = throttledStream.Read(buffer, 0, 5);

        // Assert
        Assert.Equal(5, bytesRead);
        Assert.Equal(data, buffer);
    }

    [Fact]
    public async Task ReadAsync_ReturnsCorrectData() {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var innerStream = new MemoryStream(data);
        using var throttledStream = CreateThrottledStream(innerStream, 1_000_000);
        var buffer = new byte[5];

        // Act
        var bytesRead = await throttledStream.ReadAsync(buffer.AsMemory(0, 5));

        // Assert
        Assert.Equal(5, bytesRead);
        Assert.Equal(data, buffer);
    }

    [Fact]
    public void Write_WritesCorrectData() {
        // Arrange
        using var innerStream = new MemoryStream();
        using var throttledStream = CreateThrottledStream(innerStream, 1_000_000);
        var data = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        throttledStream.Write(data, 0, 5);

        // Assert
        innerStream.Position = 0;
        var result = new byte[5];
        innerStream.Read(result, 0, 5);
        Assert.Equal(data, result);
    }

    [Fact]
    public async Task WriteAsync_WritesCorrectData() {
        // Arrange
        using var innerStream = new MemoryStream();
        using var throttledStream = CreateThrottledStream(innerStream, 1_000_000);
        var data = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        await throttledStream.WriteAsync(data.AsMemory(0, 5));

        // Assert
        innerStream.Position = 0;
        var result = new byte[5];
        await innerStream.ReadAsync(result.AsMemory(0, 5));
        Assert.Equal(data, result);
    }

    [Fact]
    public async Task ReadAsync_WithThrottling_DelaysTransfer() {
        // Arrange - Set a very low rate: 100 bytes/second for 500 bytes = 5 seconds minimum
        // But we'll use 500 bytes/second for 1000 bytes = 2 seconds minimum
        var data = new byte[1000];
        for (int i = 0; i < data.Length; i++) {
            data[i] = (byte)(i % 256);
        }

        using var innerStream = new MemoryStream(data);
        // 500 bytes per second - should take at least 2 seconds for 1000 bytes
        using var throttledStream = CreateThrottledStream(innerStream, 500);
        var buffer = new byte[1000];

        // Act
        var sw = Stopwatch.StartNew();
        var bytesRead = await throttledStream.ReadAsync(buffer.AsMemory(0, 1000));
        sw.Stop();

        // Assert - Should take at least 1 second (with some tolerance for timing)
        Assert.Equal(1000, bytesRead);
        Assert.True(sw.ElapsedMilliseconds >= 800, $"Expected at least 800ms, but took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void CanRead_ReflectsInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3]);
        using var throttledStream = CreateThrottledStream(innerStream, 1_000_000);

        // Assert
        Assert.Equal(innerStream.CanRead, throttledStream.CanRead);
    }

    [Fact]
    public void CanSeek_ReflectsInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3]);
        using var throttledStream = CreateThrottledStream(innerStream, 1_000_000);

        // Assert
        Assert.Equal(innerStream.CanSeek, throttledStream.CanSeek);
    }

    [Fact]
    public void CanWrite_ReflectsInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3]);
        using var throttledStream = CreateThrottledStream(innerStream, 1_000_000);

        // Assert
        Assert.Equal(innerStream.CanWrite, throttledStream.CanWrite);
    }

    [Fact]
    public void Length_ReflectsInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3, 4, 5]);
        using var throttledStream = CreateThrottledStream(innerStream, 1_000_000);

        // Assert
        Assert.Equal(5, throttledStream.Length);
    }

    [Fact]
    public void Position_GetAndSet_ReflectsInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3, 4, 5]);
        using var throttledStream = CreateThrottledStream(innerStream, 1_000_000);

        // Act
        throttledStream.Position = 3;

        // Assert
        Assert.Equal(3, throttledStream.Position);
        Assert.Equal(3, innerStream.Position);
    }

    [Fact]
    public void Seek_DelegatesToInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3, 4, 5]);
        using var throttledStream = CreateThrottledStream(innerStream, 1_000_000);

        // Act
        var newPosition = throttledStream.Seek(2, SeekOrigin.Begin);

        // Assert
        Assert.Equal(2, newPosition);
        Assert.Equal(2, innerStream.Position);
    }

    [Fact]
    public void Flush_DelegatesToInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3]);
        using var throttledStream = CreateThrottledStream(innerStream, 1_000_000);

        // Act & Assert - should not throw
        throttledStream.Flush();
    }

    [Fact]
    public async Task FlushAsync_DelegatesToInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3]);
        using var throttledStream = CreateThrottledStream(innerStream, 1_000_000);

        // Act & Assert - should not throw
        await throttledStream.FlushAsync();
    }

    [Fact]
    public void SetLength_DelegatesToInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream();
        using var throttledStream = CreateThrottledStream(innerStream, 1_000_000);

        // Act
        throttledStream.SetLength(100);

        // Assert
        Assert.Equal(100, innerStream.Length);
        Assert.Equal(100, throttledStream.Length);
    }

    [Fact]
    public void Dispose_DisposesInnerStream() {
        // Arrange
        var innerStream = new MemoryStream([1, 2, 3]);
        var throttledStream = CreateThrottledStream(innerStream, 1_000_000);

        // Act
        throttledStream.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => innerStream.ReadByte());
    }

    [Fact]
    public async Task ReadAsync_WithCancellation_RespectsCancellationToken() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3, 4, 5]);
        // Use a very low rate to ensure we hit the delay
        using var throttledStream = CreateThrottledStream(innerStream, 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var buffer = new byte[5];

        // Act & Assert
#pragma warning disable CA2022, CA1835 // Testing cancellation behavior
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await throttledStream.ReadAsync(buffer, 0, buffer.Length, cts.Token));
#pragma warning restore CA2022, CA1835
    }

    [Fact]
    public void Read_WithHighBandwidth_NoSignificantDelay() {
        // Arrange - High bandwidth should not cause significant delays
        var data = new byte[1000];
        using var innerStream = new MemoryStream(data);
        // 100 MB/s - should be essentially instant
        using var throttledStream = CreateThrottledStream(innerStream, 100_000_000);
        var buffer = new byte[1000];

        // Act
        var sw = Stopwatch.StartNew();
        var bytesRead = throttledStream.Read(buffer, 0, 1000);
        sw.Stop();

        // Assert
        Assert.Equal(1000, bytesRead);
        // Should complete in under 100ms for such a high bandwidth
        Assert.True(sw.ElapsedMilliseconds < 100, $"Expected < 100ms, but took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Read_MultipleSmallReads_AccumulatesCorrectly() {
        // Arrange
        var data = new byte[100];
        for (int i = 0; i < data.Length; i++) {
            data[i] = (byte)i;
        }

        using var innerStream = new MemoryStream(data);
        using var throttledStream = CreateThrottledStream(innerStream, 1_000_000);
        var allBytesRead = new List<byte>();
        var buffer = new byte[10];

        // Act - Read in 10-byte chunks
        int bytesRead;
        while ((bytesRead = throttledStream.Read(buffer, 0, 10)) > 0) {
            allBytesRead.AddRange(buffer.Take(bytesRead));
        }

        // Assert
        Assert.Equal(data, allBytesRead.ToArray());
    }

    // Helper method to create ThrottledStream using reflection since it's internal
    private static Stream CreateThrottledStream(Stream innerStream, long maxBytesPerSecond) {
        var assembly = typeof(LocalFileStorage).Assembly;
        var throttledStreamType = assembly.GetType("Oire.SharpSync.Storage.ThrottledStream");
        if (throttledStreamType is null) {
            throw new InvalidOperationException("ThrottledStream type not found");
        }

        return (Stream)Activator.CreateInstance(throttledStreamType, innerStream, maxBytesPerSecond)!;
    }
}
