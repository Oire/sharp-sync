# PowerShell script to prepare native CSync libraries for NuGet packaging
# This script creates the directory structure but does not download the actual binaries

param(
    [string]$OutputPath = "../src/SharpSync/runtimes"
)

$ErrorActionPreference = "Stop"

# Create directory structure
$platforms = @(
    "win-x64/native",
    "win-x86/native", 
    "linux-x64/native",
    "linux-arm64/native",
    "osx-x64/native",
    "osx-arm64/native"
)

Write-Host "Creating directory structure for native libraries..." -ForegroundColor Green

foreach ($platform in $platforms) {
    $fullPath = Join-Path $OutputPath $platform
    if (!(Test-Path $fullPath)) {
        New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
        Write-Host "Created: $fullPath"
    }
}

Write-Host "`nDirectory structure created successfully!" -ForegroundColor Green
Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "1. Download or compile CSync binaries for each platform"
Write-Host "2. Place the binaries in the appropriate directories:"
Write-Host "   - Windows x64: $OutputPath/win-x64/native/csync.dll"
Write-Host "   - Windows x86: $OutputPath/win-x86/native/csync.dll"
Write-Host "   - Linux x64: $OutputPath/linux-x64/native/libcsync.so"
Write-Host "   - Linux ARM64: $OutputPath/linux-arm64/native/libcsync.so"
Write-Host "   - macOS x64: $OutputPath/osx-x64/native/libcsync.dylib"
Write-Host "   - macOS ARM64: $OutputPath/osx-arm64/native/libcsync.dylib"
Write-Host "`nNote: The actual CSync binaries need to be obtained from:"
Write-Host "  - Official CSync releases: https://github.com/csync/csync/releases"
Write-Host "  - Or compiled from source: https://github.com/csync/csync"