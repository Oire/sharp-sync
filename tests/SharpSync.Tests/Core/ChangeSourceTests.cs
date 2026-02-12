namespace Oire.SharpSync.Tests.Core;

/// <summary>
/// Tests for the ChangeSource enum.
/// </summary>
public class ChangeSourceTests {
    [Fact]
    public void Local_HasExpectedValue() {
        Assert.Equal(0, (int)ChangeSource.Local);
    }

    [Fact]
    public void Remote_HasExpectedValue() {
        Assert.Equal(1, (int)ChangeSource.Remote);
    }

    [Theory]
    [InlineData(ChangeSource.Local)]
    [InlineData(ChangeSource.Remote)]
    public void AllValues_AreDefinedAndDistinct(ChangeSource source) {
        Assert.True(Enum.IsDefined(source));
    }

    [Fact]
    public void AllValues_Count() {
        var values = Enum.GetValues<ChangeSource>();
        Assert.Equal(2, values.Length);
    }
}
