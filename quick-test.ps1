# JavScraper Quick Test Script
# Simple and fast validation without full Emby installation

param(
    [switch]$Compile,
    [switch]$Syntax,
    [switch]$Structure,
    [switch]$All
)

Write-Host "=== JavScraper Quick Test ===" -ForegroundColor Green
Write-Host ""

function Write-Result {
    param($Message, $Success)
    $symbol = if ($Success) { "[PASS]" } else { "[FAIL]" }
    $color = if ($Success) { "Green" } else { "Red" }
    Write-Host "$symbol $Message" -ForegroundColor $color
}

function Test-ProjectStructure {
    Write-Host "Testing project structure..." -ForegroundColor Cyan
    
    $requiredFiles = @(
        "Emby.Plugins.JavScraper\Plugin.cs",
        "Emby.Plugins.JavScraper\JavMovieProvider.cs",
        "Emby.Plugins.JavScraper\Scrapers\JavBus.cs",
        "Emby.Plugins.JavScraper\Scrapers\JavDB.cs"
    )
    
    $removedFiles = @(
        "Jellyfin.GenerateConfigurationPage",
        "Emby.Plugins.JavScraper\Scrapers\FC2.cs",
        "Emby.Plugins.JavScraper\Scrapers\AVSOX.cs"
    )
    
    $allGood = $true
    
    foreach ($file in $requiredFiles) {
        $exists = Test-Path $file
        Write-Result "Required file: $file" $exists
        if (-not $exists) { $allGood = $false }
    }
    
    foreach ($file in $removedFiles) {
        $exists = Test-Path $file
        Write-Result "Removed file: $file" (-not $exists)
        if ($exists) { $allGood = $false }
    }
    
    return $allGood
}

function Test-BasicSyntax {
    Write-Host "Testing basic syntax..." -ForegroundColor Cyan
    
    $csFiles = Get-ChildItem "Emby.Plugins.JavScraper" -Recurse -Include "*.cs"
    $allGood = $true
    
    foreach ($file in $csFiles) {
        $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
        if ($content) {
            # Check for Jellyfin references
            if ($content -match '__JELLYFIN__') {
                Write-Result "No Jellyfin refs in $($file.Name)" $false
                $allGood = $false
            }
            
            # Basic bracket matching
            $openBraces = ($content -split '\{').Count - 1
            $closeBraces = ($content -split '\}').Count - 1
            if ($openBraces -ne $closeBraces) {
                Write-Result "Bracket balance in $($file.Name)" $false
                $allGood = $false
            }
        }
    }
    
    if ($allGood) {
        Write-Result "Basic syntax checks" $true
    }
    
    return $allGood
}

function Test-Compilation {
    Write-Host "Testing compilation..." -ForegroundColor Cyan
    
    $dotnetPath = Get-Command "dotnet" -ErrorAction SilentlyContinue
    if (-not $dotnetPath) {
        Write-Result "Compilation test (dotnet not found)" $false
        return $false
    }
    
    try {
        $output = & dotnet build "Emby.Plugins.JavScraper\Emby.Plugins.JavScraper.csproj" --configuration Debug --verbosity quiet 2>&1
        $success = $LASTEXITCODE -eq 0
        
        Write-Result "Project compilation" $success
        
        if (-not $success) {
            Write-Host "Compilation errors:" -ForegroundColor Red
            $output | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        }
        
        return $success
    }
    catch {
        Write-Result "Compilation test (exception)" $false
        return $false
    }
}

# Main execution
$results = @{}

if ($All -or $Structure) {
    $results["Structure"] = Test-ProjectStructure
    Write-Host ""
}

if ($All -or $Syntax) {
    $results["Syntax"] = Test-BasicSyntax
    Write-Host ""
}

if ($All -or $Compile) {
    $results["Compilation"] = Test-Compilation
    Write-Host ""
}

if (-not ($Structure -or $Syntax -or $Compile)) {
    # Default: run all tests
    $results["Structure"] = Test-ProjectStructure
    Write-Host ""
    $results["Syntax"] = Test-BasicSyntax
    Write-Host ""
    $results["Compilation"] = Test-Compilation
    Write-Host ""
}

# Summary
$totalTests = $results.Count
$passedTests = ($results.Values | Where-Object { $_ -eq $true }).Count
$failedTests = $totalTests - $passedTests

Write-Host "=== Summary ===" -ForegroundColor Yellow
Write-Host "Total tests: $totalTests" -ForegroundColor Cyan
Write-Host "Passed: $passedTests" -ForegroundColor Green
Write-Host "Failed: $failedTests" -ForegroundColor $(if ($failedTests -eq 0) { "Green" } else { "Red" })

if ($failedTests -eq 0) {
    Write-Host ""
    Write-Host "SUCCESS: All tests passed! Ready for Emby deployment." -ForegroundColor Green
    exit 0
} else {
    Write-Host ""
    Write-Host "ERROR: Some tests failed. Please fix the issues." -ForegroundColor Red
    exit 1
}
