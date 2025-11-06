namespace Oire.SharpSync.Tests.Storage;

public class ProgressStreamTests {
    [Fact]
    public void Constructor_ValidParameters_InitializesCorrectly() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3, 4, 5]);
        var progressCallbackInvoked = false;
        Action<long, long> progressCallback = (current, total) => {
            progressCallbackInvoked = true;
        };

        // Act
        using var progressStream = CreateProgressStream(innerStream, 5, progressCallback);

        // Assert
        Assert.NotNull(progressStream);
        Assert.True(progressCallbackInvoked); // Constructor should report initial progress
    }

    [Fact]
    public void Constructor_NullInnerStream_ThrowsArgumentNullException() {
        // Arrange
        Action<long, long> progressCallback = (current, total) => { };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            CreateProgressStream(null!, 100, progressCallback));
    }

    [Fact]
    public void Constructor_NullProgressCallback_ThrowsArgumentNullException() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3]);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            CreateProgressStream(innerStream, 3, null!));
    }

    [Fact]
    public void Constructor_ReportsInitialProgress() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3, 4, 5]);
        long reportedCurrent = -1;
        long reportedTotal = -1;
        Action<long, long> progressCallback = (current, total) => {
            reportedCurrent = current;
            reportedTotal = total;
        };

        // Act
        using var progressStream = CreateProgressStream(innerStream, 5, progressCallback);

        // Assert
        Assert.Equal(0, reportedCurrent);
        Assert.Equal(5, reportedTotal);
    }

    [Fact]
    public void Read_ReportsProgress() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3, 4, 5]);
        var progressReports = new List<(long current, long total)>();
        Action<long, long> progressCallback = (current, total) => {
            progressReports.Add((current, total));
        };

        using var progressStream = CreateProgressStream(innerStream, 5, progressCallback);
        var buffer = new byte[3];

        // Act
        var bytesRead = progressStream.Read(buffer, 0, 3);

        // Assert
        Assert.Equal(3, bytesRead);
        Assert.True(progressReports.Count >= 2); // Initial + after read
        Assert.Equal(3, progressReports[^1].current); // Last report should show 3 bytes read
        Assert.Equal(5, progressReports[^1].total);
    }

    [Fact]
    public async Task ReadAsync_ReportsProgress() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3, 4, 5]);
        var progressReports = new List<(long current, long total)>();
        Action<long, long> progressCallback = (current, total) => {
            progressReports.Add((current, total));
        };

        using var progressStream = CreateProgressStream(innerStream, 5, progressCallback);
        var buffer = new byte[3];

        // Act
        var bytesRead = await progressStream.ReadAsync(buffer, 0, 3);

        // Assert
        Assert.Equal(3, bytesRead);
        Assert.True(progressReports.Count >= 2); // Initial + after read
        Assert.Equal(3, progressReports[^1].current);
        Assert.Equal(5, progressReports[^1].total);
    }

    [Fact]
    public async Task ReadAsync_WithMemory_ReportsProgress() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3, 4, 5]);
        var progressReports = new List<(long current, long total)>();
        Action<long, long> progressCallback = (current, total) => {
            progressReports.Add((current, total));
        };

        using var progressStream = CreateProgressStream(innerStream, 5, progressCallback);
        var buffer = new byte[3];

        // Act
        var bytesRead = await progressStream.ReadAsync(buffer.AsMemory(0, 3));

        // Assert
        Assert.Equal(3, bytesRead);
        Assert.True(progressReports.Count >= 2);
        Assert.Equal(3, progressReports[^1].current);
        Assert.Equal(5, progressReports[^1].total);
    }

    [Fact]
    public void Read_MultipleReads_AccumulatesProgress() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var progressReports = new List<(long current, long total)>();
        Action<long, long> progressCallback = (current, total) => {
            progressReports.Add((current, total));
        };

        using var progressStream = CreateProgressStream(innerStream, 10, progressCallback);
        var buffer = new byte[3];

        // Act
        progressStream.Read(buffer, 0, 3); // Read 3 bytes
        progressStream.Read(buffer, 0, 3); // Read 3 more bytes
        progressStream.Read(buffer, 0, 3); // Read 3 more bytes

        // Assert
        var lastReport = progressReports[^1];
        Assert.Equal(9, lastReport.current); // Total 9 bytes read
        Assert.Equal(10, lastReport.total);
    }

    [Fact]
    public void Read_ZeroBytes_DoesNotReportProgress() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3]);
        var progressCount = 0;
        Action<long, long> progressCallback = (current, total) => {
            progressCount++;
        };

        using var progressStream = CreateProgressStream(innerStream, 3, progressCallback);
        var buffer = new byte[3];

        // Move to end so next read returns 0
        progressStream.Read(buffer, 0, 3);
        var initialCount = progressCount;

        // Act
        var bytesRead = progressStream.Read(buffer, 0, 3); // Should read 0 bytes

        // Assert
        Assert.Equal(0, bytesRead);
        Assert.Equal(initialCount, progressCount); // No additional progress reported for 0 bytes
    }

    [Fact]
    public void CanRead_ReflectsInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3]);
        using var progressStream = CreateProgressStream(innerStream, 3, (c, t) => { });

        // Act & Assert
        Assert.Equal(innerStream.CanRead, progressStream.CanRead);
    }

    [Fact]
    public void CanSeek_ReflectsInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3]);
        using var progressStream = CreateProgressStream(innerStream, 3, (c, t) => { });

        // Act & Assert
        Assert.Equal(innerStream.CanSeek, progressStream.CanSeek);
    }

    [Fact]
    public void CanWrite_ReflectsInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3]);
        using var progressStream = CreateProgressStream(innerStream, 3, (c, t) => { });

        // Act & Assert
        Assert.Equal(innerStream.CanWrite, progressStream.CanWrite);
    }

    [Fact]
    public void Length_ReflectsInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3, 4, 5]);
        using var progressStream = CreateProgressStream(innerStream, 5, (c, t) => { });

        // Act & Assert
        Assert.Equal(innerStream.Length, progressStream.Length);
    }

    [Fact]
    public void Position_GetAndSet_ReflectsInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3, 4, 5]);
        using var progressStream = CreateProgressStream(innerStream, 5, (c, t) => { });

        // Act
        progressStream.Position = 3;

        // Assert
        Assert.Equal(3, progressStream.Position);
        Assert.Equal(3, innerStream.Position);
    }

    [Fact]
    public void Seek_DelegatesToInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3, 4, 5]);
        using var progressStream = CreateProgressStream(innerStream, 5, (c, t) => { });

        // Act
        var newPosition = progressStream.Seek(2, SeekOrigin.Begin);

        // Assert
        Assert.Equal(2, newPosition);
        Assert.Equal(2, innerStream.Position);
    }

    [Fact]
    public void Flush_DelegatesToInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3]);
        using var progressStream = CreateProgressStream(innerStream, 3, (c, t) => { });

        // Act & Assert - should not throw
        progressStream.Flush();
    }

    [Fact]
    public async Task FlushAsync_DelegatesToInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3]);
        using var progressStream = CreateProgressStream(innerStream, 3, (c, t) => { });

        // Act & Assert - should not throw
        await progressStream.FlushAsync();
    }

    [Fact]
    public void Write_DelegatesToInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream();
        using var progressStream = CreateProgressStream(innerStream, 10, (c, t) => { });
        var data = new byte[] { 1, 2, 3 };

        // Act
        progressStream.Write(data, 0, 3);

        // Assert
        innerStream.Position = 0;
        var written = new byte[3];
        innerStream.Read(written, 0, 3);
        Assert.Equal(data, written);
    }

    [Fact]
    public async Task WriteAsync_DelegatesToInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream();
        using var progressStream = CreateProgressStream(innerStream, 10, (c, t) => { });
        var data = new byte[] { 1, 2, 3 };

        // Act
        await progressStream.WriteAsync(data, 0, 3);

        // Assert
        innerStream.Position = 0;
        var written = new byte[3];
        await innerStream.ReadAsync(written, 0, 3);
        Assert.Equal(data, written);
    }

    [Fact]
    public async Task WriteAsync_WithReadOnlyMemory_DelegatesToInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream();
        using var progressStream = CreateProgressStream(innerStream, 10, (c, t) => { });
        var data = new byte[] { 1, 2, 3 };

        // Act
        await progressStream.WriteAsync(new ReadOnlyMemory<byte>(data));

        // Assert
        innerStream.Position = 0;
        var written = new byte[3];
        await innerStream.ReadAsync(written, 0, 3);
        Assert.Equal(data, written);
    }

    [Fact]
    public void SetLength_DelegatesToInnerStream() {
        // Arrange
        using var innerStream = new MemoryStream();
        using var progressStream = CreateProgressStream(innerStream, 10, (c, t) => { });

        // Act
        progressStream.SetLength(100);

        // Assert
        Assert.Equal(100, innerStream.Length);
        Assert.Equal(100, progressStream.Length);
    }

    [Fact]
    public void Dispose_DisposesInnerStream() {
        // Arrange
        var innerStream = new MemoryStream([1, 2, 3]);
        var progressStream = CreateProgressStream(innerStream, 3, (c, t) => { });

        // Act
        progressStream.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => innerStream.ReadByte());
    }

    [Fact]
    public async Task ReadAsync_WithCancellation_RespectsCancellationToken() {
        // Arrange
        using var innerStream = new MemoryStream([1, 2, 3, 4, 5]);
        using var progressStream = CreateProgressStream(innerStream, 5, (c, t) => { });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var buffer = new byte[5];

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await progressStream.ReadAsync(buffer.AsMemory(), cts.Token));
    }

    [Fact]
    public void Read_LargeFile_TracksProgressCorrectly() {
        // Arrange
        var largeData = new byte[10000];
        for (int i = 0; i < largeData.Length; i++) {
            largeData[i] = (byte)(i % 256);
        }

        using var innerStream = new MemoryStream(largeData);
        var progressReports = new List<(long current, long total)>();
        Action<long, long> progressCallback = (current, total) => {
            progressReports.Add((current, total));
        };

        using var progressStream = CreateProgressStream(innerStream, largeData.Length, progressCallback);
        var buffer = new byte[1000];

        // Act
        while (progressStream.Read(buffer, 0, buffer.Length) > 0) {
            // Read entire stream
        }

        // Assert
        var lastReport = progressReports[^1];
        Assert.Equal(10000, lastReport.current);
        Assert.Equal(10000, lastReport.total);
    }

    // Helper method to create ProgressStream using reflection since it's internal
    private static Stream CreateProgressStream(Stream innerStream, long totalLength, Action<long, long> progressCallback) {
        var assembly = typeof(LocalFileStorage).Assembly;
        var progressStreamType = assembly.GetType("Oire.SharpSync.Storage.ProgressStream");
        if (progressStreamType == null) {
            throw new InvalidOperationException("ProgressStream type not found");
        }

        return (Stream)Activator.CreateInstance(progressStreamType, innerStream, totalLength, progressCallback)!;
    }
}
