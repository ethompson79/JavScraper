# JavScraper Independent Testing Tool
# Usage: 
#   .\RunTest.ps1                    # Interactive mode
#   .\RunTest.ps1 PRED-066          # Test single movie ID
#   .\RunTest.ps1 PRED-066 SSNI-123 # Test multiple movie IDs

param(
    [string[]]$MovieIds = @()
)

Write-Host "JavScraper Independent Testing Tool" -ForegroundColor Green
Write-Host $("=" * 50) -ForegroundColor Gray

# Check .NET installation
try {
    $dotnetVersion = dotnet --version 2>$null
    if ($dotnetVersion) {
        Write-Host "✅ .NET Version: $dotnetVersion" -ForegroundColor Green
    } else {
        Write-Host "❌ .NET SDK not found, please install .NET 6.0 or higher" -ForegroundColor Red
        Write-Host "Download: https://dotnet.microsoft.com/download" -ForegroundColor Yellow
        exit 1
    }
} catch {
    Write-Host "❌ Cannot detect .NET SDK" -ForegroundColor Red
    exit 1
}

# Check project files
if (-not (Test-Path "ScraperTest.csproj")) {
    Write-Host "❌ Project file not found: ScraperTest.csproj" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path "JavScraperTester.cs")) {
    Write-Host "❌ Source file not found: JavScraperTester.cs" -ForegroundColor Red
    exit 1
}

Write-Host "Restoring NuGet packages..." -ForegroundColor Cyan
try {
    dotnet restore ScraperTest.csproj 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ NuGet packages restored successfully" -ForegroundColor Green
    } else {
        Write-Host "⚠️ NuGet restore may have issues, but continuing..." -ForegroundColor Yellow
    }
} catch {
    Write-Host "⚠️ NuGet restore failed, but continuing..." -ForegroundColor Yellow
}

Write-Host "Starting scraper test program..." -ForegroundColor Cyan
Write-Host ""

# Create test results folder
$testFolder = "TestResults"
if (-not (Test-Path $testFolder)) {
    New-Item -ItemType Directory -Path $testFolder | Out-Null
    Write-Host "Created test results folder: $testFolder" -ForegroundColor Gray
}

try {
    if ($MovieIds.Count -gt 0) {
        # Command line mode - test specified movie IDs
        Write-Host "Batch test mode: $($MovieIds -join ', ')" -ForegroundColor Yellow
        dotnet run --project ScraperTest.csproj -- $MovieIds
    } else {
        # Interactive mode
        Write-Host "Interactive mode: You can input movie IDs for real-time testing" -ForegroundColor Yellow
        dotnet run --project ScraperTest.csproj
    }
} catch {
    Write-Host "❌ Program failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Please check network connection and .NET environment" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Test results saved to: $((Get-Location).Path)\$testFolder" -ForegroundColor Green
Write-Host "Press Enter to exit..." -ForegroundColor Gray
Read-Host 