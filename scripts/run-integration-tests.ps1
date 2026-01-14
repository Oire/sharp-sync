# Script to run SharpSync integration tests with Docker-based test servers (SFTP, FTP, S3/LocalStack, WebDAV)
# Usage: .\scripts\run-integration-tests.ps1 [-TestFilter "filter"]

param(
    [string]$TestFilter = ""
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

Write-Host "🚀 Starting test servers (SFTP, FTP, S3/LocalStack, WebDAV)..." -ForegroundColor Cyan
Set-Location $ProjectRoot
docker-compose -f docker-compose.test.yml up -d

Write-Host "⏳ Waiting for SFTP server to be ready..." -ForegroundColor Yellow
# Wait up to 60 seconds for the server to be healthy
$timeout = 60
$elapsed = 0
$isHealthy = $false

while ($elapsed -lt $timeout) {
    $containerStatus = docker-compose -f docker-compose.test.yml ps sftp
    if ($containerStatus -match "healthy") {
        Write-Host "✅ SFTP server is ready" -ForegroundColor Green
        $isHealthy = $true
        break
    }
    Start-Sleep -Seconds 2
    $elapsed += 2
    Write-Host "   Waiting... ($elapsed`s/$timeout`s)" -ForegroundColor Gray
}

# Final check for SFTP
if (-not $isHealthy) {
    Write-Host "❌ SFTP server failed to become healthy within $timeout`s" -ForegroundColor Red
    Write-Host "📋 Server logs:" -ForegroundColor Yellow
    docker-compose -f docker-compose.test.yml logs
    docker-compose -f docker-compose.test.yml down
    exit 1
}

Write-Host "⏳ Waiting for FTP server to be ready..." -ForegroundColor Yellow
$elapsed = 0
$isHealthy = $false

while ($elapsed -lt $timeout) {
    $containerStatus = docker-compose -f docker-compose.test.yml ps ftp
    if ($containerStatus -match "healthy") {
        Write-Host "✅ FTP server is ready" -ForegroundColor Green
        $isHealthy = $true
        break
    }
    Start-Sleep -Seconds 2
    $elapsed += 2
    Write-Host "   Waiting... ($elapsed`s/$timeout`s)" -ForegroundColor Gray
}

Write-Host "⏳ Waiting for LocalStack (S3) to be ready..." -ForegroundColor Yellow
$elapsed = 0
$isHealthy = $false

while ($elapsed -lt $timeout) {
    $containerStatus = docker-compose -f docker-compose.test.yml ps localstack
    if ($containerStatus -match "healthy") {
        Write-Host "✅ LocalStack is ready" -ForegroundColor Green
        $isHealthy = $true
        break
    }
    Start-Sleep -Seconds 2
    $elapsed += 2
    Write-Host "   Waiting... ($elapsed`s/$timeout`s)" -ForegroundColor Gray
}

Write-Host "⏳ Waiting for WebDAV server to be ready..." -ForegroundColor Yellow
$elapsed = 0
$isHealthy = $false

while ($elapsed -lt $timeout) {
    $containerStatus = docker-compose -f docker-compose.test.yml ps webdav
    if ($containerStatus -match "healthy") {
        Write-Host "✅ WebDAV server is ready" -ForegroundColor Green
        $isHealthy = $true
        break
    }
    Start-Sleep -Seconds 2
    $elapsed += 2
    Write-Host "   Waiting... ($elapsed`s/$timeout`s)" -ForegroundColor Gray
}

# Create S3 test bucket in LocalStack
Write-Host "📦 Creating S3 test bucket..." -ForegroundColor Cyan
docker-compose -f docker-compose.test.yml exec -T localstack awslocal s3 mb s3://test-bucket 2>$null

# Set environment variables for tests
$env:SFTP_TEST_HOST = "localhost"
$env:SFTP_TEST_PORT = "2222"
$env:SFTP_TEST_USER = "testuser"
$env:SFTP_TEST_PASS = "testpass"
$env:SFTP_TEST_ROOT = "upload"

$env:FTP_TEST_HOST = "localhost"
$env:FTP_TEST_PORT = "21"
$env:FTP_TEST_USER = "testuser"
$env:FTP_TEST_PASS = "testpass"
$env:FTP_TEST_ROOT = "/"

$env:S3_TEST_BUCKET = "test-bucket"
$env:S3_TEST_ACCESS_KEY = "test"
$env:S3_TEST_SECRET_KEY = "test"
$env:S3_TEST_ENDPOINT = "http://localhost:4566"
$env:S3_TEST_PREFIX = "sharpsync-tests"

$env:WEBDAV_TEST_URL = "http://localhost:8080/"
$env:WEBDAV_TEST_USER = "testuser"
$env:WEBDAV_TEST_PASS = "testpass"
$env:WEBDAV_TEST_ROOT = ""

Write-Host "🧪 Running tests..." -ForegroundColor Cyan

# Run tests with optional filter
$testExitCode = 0
try {
    if ($TestFilter) {
        Write-Host "   Filter: $TestFilter" -ForegroundColor Gray
        dotnet test --verbosity normal --filter $TestFilter
    } else {
        dotnet test --verbosity normal
    }
    $testExitCode = $LASTEXITCODE
} catch {
    $testExitCode = 1
}

Write-Host "🧹 Cleaning up..." -ForegroundColor Cyan
docker-compose -f docker-compose.test.yml down -v

if ($testExitCode -eq 0) {
    Write-Host "✅ All tests passed!" -ForegroundColor Green
} else {
    Write-Host "❌ Tests failed with exit code $testExitCode" -ForegroundColor Red
}

exit $testExitCode
