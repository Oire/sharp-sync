using Xunit;

namespace Oire.SharpSync.Tests;

public class ExceptionTests
{
    [Fact]
    public void SyncException_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var errorCode = SyncErrorCode.InvalidPath;
        var message = "Test error message";

        // Act
        var exception = new SyncException(errorCode, message);

        // Assert
        Assert.Equal(errorCode, exception.ErrorCode);
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void SyncException_WithInnerException_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var errorCode = SyncErrorCode.PermissionDenied;
        var message = "Test error message";
        var innerException = new System.IO.IOException("Inner exception");

        // Act
        var exception = new SyncException(errorCode, message, innerException);

        // Assert
        Assert.Equal(errorCode, exception.ErrorCode);
        Assert.Equal(message, exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void InvalidPathException_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var path = "/invalid/path";
        var message = "Invalid path specified";

        // Act
        var exception = new InvalidPathException(path, message);

        // Assert
        Assert.Equal(SyncErrorCode.InvalidPath, exception.ErrorCode);
        Assert.Equal(path, exception.Path);
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void PermissionDeniedException_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var path = "/restricted/path";
        var message = "Access denied";

        // Act
        var exception = new PermissionDeniedException(path, message);

        // Assert
        Assert.Equal(SyncErrorCode.PermissionDenied, exception.ErrorCode);
        Assert.Equal(path, exception.Path);
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void FileConflictException_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var sourcePath = "/source/file.txt";
        var targetPath = "/target/file.txt";
        var message = "File conflict detected";

        // Act
        var exception = new FileConflictException(sourcePath, targetPath, message);

        // Assert
        Assert.Equal(SyncErrorCode.Conflict, exception.ErrorCode);
        Assert.Equal(sourcePath, exception.SourcePath);
        Assert.Equal(targetPath, exception.TargetPath);
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void FileNotFoundException_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var fileName = "missing.txt";
        var message = "File not found";

        // Act
        var exception = new FileNotFoundException(fileName, message);

        // Assert
        Assert.Equal(SyncErrorCode.FileNotFound, exception.ErrorCode);
        Assert.Equal(fileName, exception.FileName);
        Assert.Equal(message, exception.Message);
    }
}