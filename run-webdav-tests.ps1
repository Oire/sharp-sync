# PowerShell script to run WebDAV integration tests
$env:WEBDAV_TEST_URL = "http://localhost:8080/"
$env:WEBDAV_TEST_USER = "testuser"
$env:WEBDAV_TEST_PASS = "testpass"
$env:WEBDAV_TEST_ROOT = ""

# Run the WebDAV tests
dotnet test tests/SharpSync.Tests/SharpSync.Tests.csproj --filter "FullyQualifiedName~WebDav" --verbosity normal