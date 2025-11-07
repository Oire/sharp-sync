# Testing Guide

This document describes how to run tests for SharpSync, including integration tests that require external services.

## Quick Start

```bash
# Run all unit tests (no external services required)
dotnet test

# Run with verbose output
dotnet test --verbosity normal
```

## Integration Tests

SharpSync includes integration tests for storage backends that require real servers (e.g., SFTP). These tests are automatically skipped unless you provide the necessary configuration.

### SFTP Integration Tests

SFTP integration tests require a running SFTP server. You have two options:

#### Option 1: Using Docker Compose (Recommended)

The easiest way to run SFTP integration tests locally:

```bash
# Start SFTP server
docker-compose -f docker-compose.test.yml up -d

# Wait for server to be ready (about 5 seconds)
sleep 5

# Run tests with environment variables
export SFTP_TEST_HOST=localhost
export SFTP_TEST_PORT=2222
export SFTP_TEST_USER=testuser
export SFTP_TEST_PASS=testpass
export SFTP_TEST_ROOT=/home/testuser/upload

dotnet test --verbosity normal

# Stop SFTP server when done
docker-compose -f docker-compose.test.yml down
```

Or use a one-liner:

```bash
docker-compose -f docker-compose.test.yml up -d && \
sleep 5 && \
SFTP_TEST_HOST=localhost SFTP_TEST_PORT=2222 SFTP_TEST_USER=testuser SFTP_TEST_PASS=testpass SFTP_TEST_ROOT=/home/testuser/upload dotnet test --verbosity normal && \
docker-compose -f docker-compose.test.yml down
```

#### Option 2: Using Your Own SFTP Server

If you have access to an SFTP server, configure it with these environment variables:

```bash
export SFTP_TEST_HOST=your-sftp-server.com
export SFTP_TEST_PORT=22
export SFTP_TEST_USER=your-username
export SFTP_TEST_PASS=your-password        # For password authentication
# OR
export SFTP_TEST_KEY=/path/to/private-key  # For key-based authentication
export SFTP_TEST_ROOT=/path/on/server

dotnet test --verbosity normal
```

**Note:** The test suite will create and clean up test directories under `SFTP_TEST_ROOT`.

#### Option 3: Using SSH Key Authentication

To test with SSH key authentication:

```bash
# Generate test key (if needed)
ssh-keygen -t rsa -b 2048 -f ~/.ssh/sharpsync_test -N ""

# Add key to your SFTP server's authorized_keys

# Run tests
export SFTP_TEST_HOST=localhost
export SFTP_TEST_PORT=2222
export SFTP_TEST_USER=testuser
export SFTP_TEST_KEY=~/.ssh/sharpsync_test
export SFTP_TEST_ROOT=/home/testuser/upload

dotnet test --verbosity normal
```

### WebDAV Integration Tests (Future)

WebDAV integration tests are planned for future releases. They will follow a similar pattern:

```bash
# Using Docker Compose
docker-compose -f docker-compose.test.yml up -d webdav

export WEBDAV_TEST_URL=http://localhost:8080/webdav
export WEBDAV_TEST_USER=testuser
export WEBDAV_TEST_PASS=testpass

dotnet test --verbosity normal
```

## Continuous Integration

The GitHub Actions workflow automatically runs all tests, including SFTP integration tests, using a Docker-based SFTP server. See `.github/workflows/dotnet.yml` for the configuration.

## Test Categories

Tests are organized into the following categories:

### Unit Tests
- No external dependencies
- Fast execution
- Always run
- Located in `tests/SharpSync.Tests/`

### Integration Tests
- Require external services (SFTP, WebDAV, etc.)
- Slower execution
- Only run when configured
- Skip automatically if environment variables not set

## Running Specific Tests

```bash
# Run tests from a specific file
dotnet test --filter "FullyQualifiedName~SftpStorageTests"

# Run a specific test method
dotnet test --filter "FullyQualifiedName~SftpStorageTests.TestConnectionAsync_ValidCredentials_ReturnsTrue"

# Run all unit tests (excluding integration tests)
# Integration tests will skip automatically without environment variables
dotnet test

# Run only integration tests
dotnet test --filter "FullyQualifiedName~IntegrationTests"
```

## Test Coverage

To generate test coverage reports:

```bash
# Install coverage tool
dotnet tool install --global dotnet-coverage

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# View coverage report
# Report will be in TestResults/{guid}/coverage.cobertura.xml
```

## Troubleshooting

### SFTP Tests Skip Automatically

**Problem:** SFTP integration tests show as "Skipped" in test results.

**Solution:** This is expected behavior when environment variables are not set. The tests are designed to skip gracefully rather than fail. Set the required environment variables to enable them.

### SFTP Connection Refused

**Problem:** Tests fail with "Connection refused" error.

**Solution:**
1. Ensure Docker service is running
2. Check if port 2222 is already in use: `lsof -i :2222` (on Unix) or `netstat -ano | findstr :2222` (on Windows)
3. Verify SFTP container is healthy: `docker-compose -f docker-compose.test.yml ps`

### SFTP Authentication Failed

**Problem:** Tests fail with "Authentication failed" error.

**Solution:**
1. Verify environment variables are set correctly
2. For key-based auth, ensure the key file exists and has correct permissions: `chmod 600 ~/.ssh/sharpsync_test`
3. Check if the key is in the correct format (OpenSSH format)

### Tests Timeout

**Problem:** Tests hang or timeout.

**Solution:**
1. Increase test timeout in test settings
2. Check network connectivity to SFTP server
3. Ensure SFTP server is not overloaded

## Writing New Integration Tests

When adding new integration tests:

1. Check for environment variables at the start of the test
2. Use `SkipIfIntegrationTestsDisabled()` helper method
3. Clean up resources after test completion
4. Use unique paths/names to avoid conflicts with parallel test execution
5. Document required environment variables in test class summary

Example:

```csharp
[Fact]
public async Task MyNewIntegrationTest() {
    SkipIfIntegrationTestsDisabled();

    _storage = CreateStorage();

    // Test code here

    // Cleanup is handled in Dispose()
}
```

## Performance Testing

For performance benchmarks, use BenchmarkDotNet:

```bash
# Run in Release mode
dotnet run -c Release --project benchmarks/SharpSync.Benchmarks
```

(Note: Benchmarks project not yet implemented)

## Additional Resources

- [xUnit Documentation](https://xunit.net/)
- [Docker Compose Documentation](https://docs.docker.com/compose/)
- [atmoz/sftp Docker Image](https://github.com/atmoz/sftp)
