namespace Oire.SharpSync.Tests.Fixtures;

public static class TestConstants
{
    public const string TestLocalPath = "/test/local/path";
    public const string TestRemotePath = "https://cloud.example.com/remote.php/dav/files/testuser/";
    public const string TestDatabasePath = "/test/sync.db";
    
    public const string TestFileName = "test-file.txt";
    public const string TestDirectoryName = "test-directory";
    
    public const string TestFileContent = "This is test file content.";
    public const string TestModifiedContent = "This is modified test file content.";
    
    public const string TestUserId = "testuser";
    public const string TestAccessToken = "test-access-token";
    
    public const int TestFileSize = 1024;
    public const int TestLargeFileSize = 10 * 1024 * 1024; // 10 MB
}