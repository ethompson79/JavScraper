# JavScraper 实时验证脚本
# 提供快速验证而无需完整编译和安装到Emby

param(
    [switch]$Watch,
    [switch]$Quick,
    [switch]$Syntax,
    [switch]$Dependencies,
    [string]$TestFile = ""
)

Write-Host "=== JavScraper 实时验证工具 ===" -ForegroundColor Green
Write-Host ""

$ErrorCount = 0
$WarningCount = 0

function Write-Status {
    param($Message, $Status = "Info")
    $color = switch ($Status) {
        "Success" { "Green" }
        "Error" { "Red" }
        "Warning" { "Yellow" }
        "Info" { "Cyan" }
        default { "White" }
    }
    Write-Host "[$((Get-Date).ToString('HH:mm:ss'))] $Message" -ForegroundColor $color
}

function Test-SyntaxOnly {
    param($FilePath)
    
    Write-Status "检查语法: $FilePath" "Info"
    
    try {
        # 使用 PowerShell 的语法检查（对于 C# 文件的基本检查）
        $content = Get-Content $FilePath -Raw
        
        # 检查基本的 C# 语法问题
        $issues = @()
        
        # 检查括号匹配
        $openBraces = ($content -split '\{').Count - 1
        $closeBraces = ($content -split '\}').Count - 1
        if ($openBraces -ne $closeBraces) {
            $issues += "Braces mismatch: { $openBraces, } $closeBraces"
        }
        
        # 检查圆括号匹配
        $openParens = ($content -split '\(').Count - 1
        $closeParens = ($content -split '\)').Count - 1
        if ($openParens -ne $closeParens) {
            $issues += "Parentheses mismatch: ( $openParens, ) $closeParens"
        }

        # 检查方括号匹配
        $openBrackets = ($content -split '\[').Count - 1
        $closeBrackets = ($content -split '\]').Count - 1
        if ($openBrackets -ne $closeBrackets) {
            $issues += "Brackets mismatch: [ $openBrackets, ] $closeBrackets"
        }
        
        # 检查是否还有 Jellyfin 条件编译
        if ($content -match '__JELLYFIN__') {
            $issues += "Found uncleaned Jellyfin conditional compilation"
        }

        # 检查常见的语法错误
        if ($content -match ';;') {
            $issues += "Found double semicolon (;;)"
        }
        
        if ($issues.Count -eq 0) {
            Write-Status "✓ 语法检查通过" "Success"
            return $true
        } else {
            Write-Status "✗ 发现语法问题:" "Error"
            foreach ($issue in $issues) {
                Write-Status "  - $issue" "Error"
            }
            return $false
        }
    }
    catch {
        Write-Status "✗ 语法检查失败: $($_.Exception.Message)" "Error"
        return $false
    }
}

function Test-QuickCompile {
    Write-Status "执行快速编译检查..." "Info"
    
    # 尝试使用 dotnet build 进行快速编译检查
    $dotnetPath = Get-Command "dotnet" -ErrorAction SilentlyContinue
    
    if ($dotnetPath) {
        try {
            $result = & dotnet build "Emby.Plugins.JavScraper\Emby.Plugins.JavScraper.csproj" --verbosity quiet --no-restore 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-Status "✓ 快速编译检查通过" "Success"
                return $true
            } else {
                Write-Status "✗ 编译错误:" "Error"
                $result | ForEach-Object { Write-Status "  $_" "Error" }
                return $false
            }
        }
        catch {
            Write-Status "✗ 编译检查异常: $($_.Exception.Message)" "Error"
            return $false
        }
    } else {
        Write-Status "⚠ 未找到 .NET CLI，跳过编译检查" "Warning"
        return $true
    }
}

function Test-Dependencies {
    Write-Status "检查依赖项..." "Info"
    
    $csprojPath = "Emby.Plugins.JavScraper\Emby.Plugins.JavScraper.csproj"
    
    if (-not (Test-Path $csprojPath)) {
        Write-Status "✗ 找不到项目文件: $csprojPath" "Error"
        return $false
    }
    
    $content = Get-Content $csprojPath -Raw
    $issues = @()
    
    # 检查关键依赖版本
    $expectedVersions = @{
        'MediaBrowser.Server.Core' = '4.8.11'
        'HtmlAgilityPack' = '1.11.70'
        'LiteDB' = '5.0.21'
        'SkiaSharp' = '2.88.8'
    }
    
    foreach ($package in $expectedVersions.Keys) {
        $expectedVersion = $expectedVersions[$package]
        if ($content -match "$package.*Version=`"([^`"]+)`"") {
            $actualVersion = $matches[1]
            if ($actualVersion -ne $expectedVersion) {
                $issues += "$package 版本不匹配: 期望 $expectedVersion, 实际 $actualVersion"
            }
        } else {
            $issues += "未找到依赖: $package"
        }
    }
    
    # 检查不应该存在的依赖
    $forbiddenPackages = @('Jellyfin.Controller')
    foreach ($package in $forbiddenPackages) {
        if ($content -match $package) {
            $issues += "发现不应该存在的依赖: $package"
        }
    }
    
    if ($issues.Count -eq 0) {
        Write-Status "✓ 依赖检查通过" "Success"
        return $true
    } else {
        Write-Status "✗ 依赖问题:" "Error"
        foreach ($issue in $issues) {
            Write-Status "  - $issue" "Error"
        }
        return $false
    }
}

function Test-FileStructure {
    Write-Status "检查文件结构..." "Info"
    
    $issues = @()
    
    # 检查应该存在的文件
    $requiredFiles = @(
        "Emby.Plugins.JavScraper\Plugin.cs",
        "Emby.Plugins.JavScraper\JavMovieProvider.cs",
        "Emby.Plugins.JavScraper\Scrapers\JavBus.cs",
        "Emby.Plugins.JavScraper\Scrapers\JavDB.cs"
    )
    
    foreach ($file in $requiredFiles) {
        if (-not (Test-Path $file)) {
            $issues += "缺少必需文件: $file"
        }
    }
    
    # 检查应该被删除的文件
    $forbiddenFiles = @(
        "Jellyfin.GenerateConfigurationPage",
        "Emby.Plugins.JavScraper\Scrapers\FC2.cs",
        "Emby.Plugins.JavScraper\Scrapers\AVSOX.cs",
        "Emby.Plugins.JavScraper\Extensions\JellyfinExtensions.cs"
    )
    
    foreach ($file in $forbiddenFiles) {
        if (Test-Path $file) {
            $issues += "应该被删除的文件仍存在: $file"
        }
    }
    
    if ($issues.Count -eq 0) {
        Write-Status "✓ 文件结构检查通过" "Success"
        return $true
    } else {
        Write-Status "✗ 文件结构问题:" "Error"
        foreach ($issue in $issues) {
            Write-Status "  - $issue" "Error"
        }
        return $false
    }
}

function Start-FileWatcher {
    Write-Status "启动文件监控模式..." "Info"
    Write-Status "监控 C# 文件变化，按 Ctrl+C 退出" "Info"
    
    $watcher = New-Object System.IO.FileSystemWatcher
    $watcher.Path = "Emby.Plugins.JavScraper"
    $watcher.Filter = "*.cs"
    $watcher.IncludeSubdirectories = $true
    $watcher.EnableRaisingEvents = $true
    
    $action = {
        $path = $Event.SourceEventArgs.FullPath
        $changeType = $Event.SourceEventArgs.ChangeType
        $fileName = Split-Path $path -Leaf
        
        Write-Status "检测到文件变化: $fileName ($changeType)" "Info"
        
        # 等待文件写入完成
        Start-Sleep -Milliseconds 500
        
        if (Test-Path $path) {
            Test-SyntaxOnly $path
        }
    }
    
    Register-ObjectEvent -InputObject $watcher -EventName "Changed" -Action $action
    Register-ObjectEvent -InputObject $watcher -EventName "Created" -Action $action
    
    try {
        while ($true) {
            Start-Sleep -Seconds 1
        }
    }
    finally {
        $watcher.EnableRaisingEvents = $false
        $watcher.Dispose()
        Get-EventSubscriber | Unregister-Event
    }
}

# 主执行逻辑
if ($Watch) {
    Start-FileWatcher
}
elseif ($TestFile -ne "") {
    if (Test-Path $TestFile) {
        Test-SyntaxOnly $TestFile
    } else {
        Write-Status "文件不存在: $TestFile" "Error"
    }
}
else {
    $allPassed = $true
    
    if ($Syntax -or (-not $Quick -and -not $Dependencies)) {
        Write-Status "=== 语法检查 ===" "Info"
        $csFiles = Get-ChildItem "Emby.Plugins.JavScraper" -Recurse -Include "*.cs"
        foreach ($file in $csFiles) {
            if (-not (Test-SyntaxOnly $file.FullName)) {
                $allPassed = $false
            }
        }
        Write-Host ""
    }
    
    if ($Dependencies -or (-not $Quick -and -not $Syntax)) {
        Write-Status "=== 依赖检查 ===" "Info"
        if (-not (Test-Dependencies)) {
            $allPassed = $false
        }
        Write-Host ""
    }
    
    if (-not $Syntax -and -not $Dependencies) {
        Write-Status "=== 文件结构检查 ===" "Info"
        if (-not (Test-FileStructure)) {
            $allPassed = $false
        }
        Write-Host ""
    }
    
    if ($Quick -or (-not $Syntax -and -not $Dependencies)) {
        Write-Status "=== 快速编译检查 ===" "Info"
        if (-not (Test-QuickCompile)) {
            $allPassed = $false
        }
        Write-Host ""
    }
    
    # 总结
    if ($allPassed) {
        Write-Status "🎉 所有检查都通过！" "Success"
    } else {
        Write-Status "❌ 发现问题需要修复" "Error"
        exit 1
    }
}

Write-Host ""
Write-Status "使用说明:" "Info"
Write-Status "  -Watch      : 启动文件监控模式，实时检查文件变化" "Info"
Write-Status "  -Quick      : 只执行快速编译检查" "Info"
Write-Status "  -Syntax     : 只执行语法检查" "Info"
Write-Status "  -Dependencies : 只执行依赖检查" "Info"
Write-Status "  -TestFile <path> : 检查单个文件" "Info"
