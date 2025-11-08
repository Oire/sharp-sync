# Script to run SharpSync integration tests with Docker-based SFTP server
# Usage: .\scripts\run-integration-tests.ps1 [-TestFilter "filter"]

param(
    [string]$TestFilter = ""
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

Write-Host "🚀 Starting SFTP test server..." -ForegroundColor Cyan
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

# Final check
if (-not $isHealthy) {
    Write-Host "❌ SFTP server failed to become healthy within $timeout`s" -ForegroundColor Red
    Write-Host "📋 Server logs:" -ForegroundColor Yellow
    docker-compose -f docker-compose.test.yml logs
    docker-compose -f docker-compose.test.yml down
    exit 1
}

# Set environment variables for tests
$env:SFTP_TEST_HOST = "localhost"
$env:SFTP_TEST_PORT = "2222"
$env:SFTP_TEST_USER = "testuser"
$env:SFTP_TEST_PASS = "testpass"
$env:SFTP_TEST_ROOT = "/home/testuser/upload"

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
