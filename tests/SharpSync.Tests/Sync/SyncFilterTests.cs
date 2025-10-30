namespace Oire.SharpSync.Tests.Sync;

public class SyncFilterTests {
    [Fact]
    public void ShouldSync_EmptyPath_ReturnsFalse() {
        // Arrange
        var filter = new SyncFilter();

        // Act & Assert
        Assert.False(filter.ShouldSync(""));
        Assert.False(filter.ShouldSync("   "));
        Assert.False(filter.ShouldSync(null!));
    }

    [Fact]
    public void ShouldSync_NoPatterns_ReturnsTrue() {
        // Arrange
        var filter = new SyncFilter();

        // Act & Assert
        Assert.True(filter.ShouldSync("test.txt"));
        Assert.True(filter.ShouldSync("folder/file.txt"));
        Assert.True(filter.ShouldSync("deep/nested/folder/file.txt"));
    }

    [Theory]
    [InlineData("*.txt", "file.txt", false)]
    [InlineData("*.txt", "file.doc", true)]
    [InlineData("temp", "temp", false)]
    [InlineData("temp", "temp/file.txt", false)]
    [InlineData("temp/", "temp", false)]
    [InlineData("temp/", "temp/file.txt", false)]
    [InlineData("*.log", "app.log", false)]
    [InlineData("*.log", "folder/app.log", false)]
    [InlineData("test*", "test.txt", false)]
    [InlineData("test*", "testing.txt", false)]
    [InlineData("test*", "another.txt", true)]
    public void ShouldSync_ExcludePatterns_WorksCorrectly(string pattern, string path, bool expected) {
        // Arrange
        var filter = new SyncFilter();
        filter.AddExclusionPattern(pattern);

        // Act
        var result = filter.ShouldSync(path);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("*.txt", "file.txt", true)]
    [InlineData("*.txt", "file.doc", false)]
    [InlineData("docs/", "docs/file.txt", true)]
    [InlineData("docs/", "other/file.txt", false)]
    [InlineData("important*", "important.txt", true)]
    [InlineData("important*", "not-important.txt", false)]
    public void ShouldSync_IncludePatterns_WorksCorrectly(string pattern, string path, bool expected) {
        // Arrange
        var filter = new SyncFilter();
        filter.AddInclusionPattern(pattern);

        // Act
        var result = filter.ShouldSync(path);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ShouldSync_IncludeOverridesExclude_WorksCorrectly() {
        // Arrange
        var filter = new SyncFilter();
        filter.AddInclusionPattern("*.txt");
        filter.AddExclusionPattern("temp.txt");

        // Act & Assert
        Assert.True(filter.ShouldSync("important.txt"));  // Included
        Assert.False(filter.ShouldSync("temp.txt"));      // Included but then excluded
        Assert.False(filter.ShouldSync("file.doc"));      // Not included
    }

    [Theory]
    [InlineData(".git", ".git", false)]
    [InlineData(".git", ".git/config", false)]
    [InlineData(".git/", ".git", false)]
    [InlineData(".git/", ".git/hooks/pre-commit", false)]
    [InlineData("node_modules", "node_modules", false)]
    [InlineData("node_modules", "node_modules/package/index.js", false)]
    [InlineData("*.tmp", "temp.tmp", false)]
    [InlineData("*.tmp", "folder/temp.tmp", false)]
    [InlineData("~*", "~backup.txt", false)]
    [InlineData("#*#", "#temp#", false)]
    public void ShouldSync_CommonExcludePatterns_WorksCorrectly(string pattern, string path, bool expected) {
        // Arrange
        var filter = new SyncFilter();
        filter.AddExclusionPattern(pattern);

        // Act
        var result = filter.ShouldSync(path);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CreateDefault_ContainsCommonExclusions() {
        // Arrange & Act
        var filter = SyncFilter.CreateDefault();

        // Assert - Test common exclusions
        Assert.False(filter.ShouldSync(".git"));
        Assert.False(filter.ShouldSync(".git/config"));
        Assert.False(filter.ShouldSync("node_modules"));
        Assert.False(filter.ShouldSync("node_modules/package/index.js"));
        Assert.False(filter.ShouldSync("bin"));
        Assert.False(filter.ShouldSync("obj"));
        Assert.False(filter.ShouldSync("temp.tmp"));
        Assert.False(filter.ShouldSync(".DS_Store"));
        Assert.False(filter.ShouldSync("Thumbs.db"));
        Assert.False(filter.ShouldSync("~backup.txt"));
        Assert.False(filter.ShouldSync("#temp#"));

        // Assert - Test allowed files
        Assert.True(filter.ShouldSync("src/Program.cs"));
        Assert.True(filter.ShouldSync("docs/README.md"));
        Assert.True(filter.ShouldSync("data/important.json"));
    }

    [Fact]
    public void Clear_RemovesAllPatterns() {
        // Arrange
        var filter = new SyncFilter();
        filter.AddExclusionPattern("*.tmp");
        filter.AddInclusionPattern("*.txt");

        // Act
        filter.Clear();

        // Assert
        Assert.True(filter.ShouldSync("temp.tmp"));  // Should not be excluded anymore
        Assert.True(filter.ShouldSync("file.doc"));  // Should not require inclusion anymore
    }

    [Theory]
    [InlineData("**/*.txt", "folder/file.txt", false)]
    [InlineData("**/*.txt", "deep/nested/folder/file.txt", false)]
    [InlineData("**/temp/**", "project/temp/file.txt", false)]
    [InlineData("**/temp/**", "temp/subfolder/file.txt", false)]
    [InlineData("src/**/*.cs", "src/Program.cs", false)]
    [InlineData("src/**/*.cs", "src/Models/User.cs", false)]
    [InlineData("src/**/*.cs", "tests/Program.cs", true)]
    public void ShouldSync_WildcardPatterns_WorksCorrectly(string pattern, string path, bool expected) {
        // Arrange
        var filter = new SyncFilter();
        filter.AddExclusionPattern(pattern);

        // Act
        var result = filter.ShouldSync(path);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ShouldSync_CaseInsensitive_WorksCorrectly() {
        // Arrange
        var filter = new SyncFilter();
        filter.AddExclusionPattern("*.TXT");

        // Act & Assert
        Assert.False(filter.ShouldSync("file.txt"));
        Assert.False(filter.ShouldSync("FILE.TXT"));
        Assert.False(filter.ShouldSync("File.Txt"));
    }

    [Fact]
    public void ShouldSync_PathSeparatorNormalization_WorksCorrectly() {
        // Arrange
        var filter = new SyncFilter();
        filter.AddExclusionPattern("temp\\file.txt");  // Windows style

        // Act & Assert
        Assert.False(filter.ShouldSync("temp/file.txt"));  // Unix style
        Assert.False(filter.ShouldSync("temp\\file.txt")); // Windows style
    }
}
