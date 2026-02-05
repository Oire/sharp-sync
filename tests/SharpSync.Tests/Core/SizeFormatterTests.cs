namespace Oire.SharpSync.Tests.Core;

public class SizeFormatterTests {
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(10240, "10.0 KB")]
    [InlineData(1024 * 1024, "1.0 MB")]
    [InlineData(1024 * 1024 + 512 * 1024, "1.5 MB")]
    [InlineData(1024 * 1024 * 1024, "1.0 GB")]
    [InlineData(1024L * 1024 * 1024 * 2, "2.0 GB")]
    [InlineData(1024L * 1024 * 1024 * 1024, "1.0 TB")]
    [InlineData(1024L * 1024 * 1024 * 1024 * 5, "5.0 TB")]
    public void Format_VariousSizes_ReturnsExpected(long bytes, string expected) {
        Assert.Equal(expected, SizeFormatter.Format(bytes));
    }

    [Fact]
    public void Format_MaxTerabytes_DoesNotOverflow() {
        // Larger than TB range stays in TB
        var result = SizeFormatter.Format(1024L * 1024 * 1024 * 1024 * 1024);
        Assert.Equal("1024.0 TB", result);
    }
}
