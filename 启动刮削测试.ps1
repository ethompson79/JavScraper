# Quick launcher for JavScraper Testing Tool
# This script automatically enters the test directory and runs the test tool

param(
    [string[]]$MovieIds = @()
)

Write-Host "JavScraper Testing Tool Launcher" -ForegroundColor Green
Write-Host $("=" * 40) -ForegroundColor Gray

# Check if test directory exists
if (-not (Test-Path "ScraperTestTool")) {
    Write-Host "❌ Test directory not found: ScraperTestTool" -ForegroundColor Red
    Write-Host "Please make sure the test tool is properly installed." -ForegroundColor Yellow
    exit 1
}

# Enter test directory
Push-Location "ScraperTestTool"

try {
    # Run the test tool
    if ($MovieIds.Count -gt 0) {
        & ".\RunTest.ps1" @MovieIds
    } else {
        & ".\RunTest.ps1"
    }
} finally {
    # Return to original directory
    Pop-Location
} 