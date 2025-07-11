# JavScraper 项目验证脚本
# PowerShell 脚本用于验证项目更新是否成功

Write-Host "=== JavScraper 项目验证脚本 ===" -ForegroundColor Green
Write-Host ""

$ErrorCount = 0
$WarningCount = 0

function Test-FileExists {
    param($Path, $ShouldExist = $true)
    
    $exists = Test-Path $Path
    if ($ShouldExist -and $exists) {
        Write-Host "✓ 文件存在: $Path" -ForegroundColor Green
        return $true
    } elseif (-not $ShouldExist -and -not $exists) {
        Write-Host "✓ 文件已删除: $Path" -ForegroundColor Green
        return $true
    } elseif ($ShouldExist -and -not $exists) {
        Write-Host "✗ 文件缺失: $Path" -ForegroundColor Red
        $script:ErrorCount++
        return $false
    } else {
        Write-Host "✗ 文件应该被删除但仍存在: $Path" -ForegroundColor Red
        $script:ErrorCount++
        return $false
    }
}

function Test-ContentContains {
    param($Path, $Pattern, $ShouldContain = $true)
    
    if (-not (Test-Path $Path)) {
        Write-Host "✗ 无法检查文件内容，文件不存在: $Path" -ForegroundColor Red
        $script:ErrorCount++
        return $false
    }
    
    $content = Get-Content $Path -Raw
    $contains = $content -match $Pattern
    
    if ($ShouldContain -and $contains) {
        Write-Host "✓ 文件包含预期内容: $Path" -ForegroundColor Green
        return $true
    } elseif (-not $ShouldContain -and -not $contains) {
        Write-Host "✓ 文件不包含不需要的内容: $Path" -ForegroundColor Green
        return $true
    } elseif ($ShouldContain -and -not $contains) {
        Write-Host "✗ 文件缺少预期内容 '$Pattern': $Path" -ForegroundColor Red
        $script:ErrorCount++
        return $false
    } else {
        Write-Host "✗ 文件包含不应该存在的内容 '$Pattern': $Path" -ForegroundColor Red
        $script:ErrorCount++
        return $false
    }
}

# 1. 检查项目文件结构
Write-Host "1. 检查项目文件结构..." -ForegroundColor Yellow

# 检查主要项目文件存在
Test-FileExists "Emby.Plugins.JavScraper\Emby.Plugins.JavScraper.csproj"
Test-FileExists "Emby.Plugins.JavScraper\Plugin.cs"
Test-FileExists "Emby.Plugins.JavScraper.sln"

# 检查已删除的 Jellyfin 相关文件
Test-FileExists "Jellyfin.GenerateConfigurationPage" $false
Test-FileExists "Emby.Plugins.JavScraper\Configuration\Jellyfin.ConfigPage.html" $false
Test-FileExists "Emby.Plugins.JavScraper\Configuration\Jellyfin.JavOrganizationConfigPage.html" $false
Test-FileExists "Emby.Plugins.JavScraper\Extensions\JellyfinExtensions.cs" $false

# 检查已删除的刮削器
Test-FileExists "Emby.Plugins.JavScraper\Scrapers\AVSOX.cs" $false
Test-FileExists "Emby.Plugins.JavScraper\Scrapers\FC2.cs" $false
Test-FileExists "Emby.Plugins.JavScraper\Scrapers\Jav123.cs" $false
Test-FileExists "Emby.Plugins.JavScraper\Scrapers\MgsTage.cs" $false
Test-FileExists "Emby.Plugins.JavScraper\Scrapers\R18.cs" $false

# 检查保留的刮削器
Test-FileExists "Emby.Plugins.JavScraper\Scrapers\JavBus.cs"
Test-FileExists "Emby.Plugins.JavScraper\Scrapers\JavDB.cs"
Test-FileExists "Emby.Plugins.JavScraper\Scrapers\Gfriends.cs"

Write-Host ""

# 2. 检查项目配置
Write-Host "2. 检查项目配置..." -ForegroundColor Yellow

# 检查 csproj 文件中的依赖版本
Test-ContentContains "Emby.Plugins.JavScraper\Emby.Plugins.JavScraper.csproj" 'MediaBrowser\.Server\.Core.*4\.8\.11'
Test-ContentContains "Emby.Plugins.JavScraper\Emby.Plugins.JavScraper.csproj" 'HtmlAgilityPack.*1\.11\.70'
Test-ContentContains "Emby.Plugins.JavScraper\Emby.Plugins.JavScraper.csproj" 'LiteDB.*5\.0\.21'

# 检查不应该存在的 Jellyfin 引用
Test-ContentContains "Emby.Plugins.JavScraper\Emby.Plugins.JavScraper.csproj" 'Jellyfin\.Controller' $false
Test-ContentContains "Emby.Plugins.JavScraper\Emby.Plugins.JavScraper.csproj" 'Debug\.Jellyfin' $false
Test-ContentContains "Emby.Plugins.JavScraper\Emby.Plugins.JavScraper.csproj" 'Release\.Jellyfin' $false

Write-Host ""

# 3. 检查代码中的条件编译
Write-Host "3. 检查条件编译指令..." -ForegroundColor Yellow

$codeFiles = Get-ChildItem "Emby.Plugins.JavScraper" -Recurse -Include "*.cs"
$jellyfinReferences = 0

foreach ($file in $codeFiles) {
    $content = Get-Content $file.FullName -Raw
    if ($content -match '__JELLYFIN__') {
        Write-Host "✗ 发现 Jellyfin 条件编译指令: $($file.FullName)" -ForegroundColor Red
        $script:ErrorCount++
        $jellyfinReferences++
    }
}

if ($jellyfinReferences -eq 0) {
    Write-Host "✓ 所有 Jellyfin 条件编译指令已清理" -ForegroundColor Green
}

Write-Host ""

# 4. 检查解决方案文件
Write-Host "4. 检查解决方案配置..." -ForegroundColor Yellow

Test-ContentContains "Emby.Plugins.JavScraper.sln" 'Jellyfin\.GenerateConfigurationPage' $false
Test-ContentContains "Emby.Plugins.JavScraper.sln" 'Debug\.Jellyfin' $false
Test-ContentContains "Emby.Plugins.JavScraper.sln" 'Release\.Jellyfin' $false

Write-Host ""

# 5. 尝试编译检查（如果有 MSBuild）
Write-Host "5. 编译检查..." -ForegroundColor Yellow

$msbuildPath = Get-Command "msbuild" -ErrorAction SilentlyContinue
$dotnetPath = Get-Command "dotnet" -ErrorAction SilentlyContinue

if ($msbuildPath) {
    Write-Host "找到 MSBuild，尝试编译..." -ForegroundColor Cyan
    try {
        $result = & msbuild "Emby.Plugins.JavScraper.sln" /p:Configuration=Release /verbosity:quiet 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓ 项目编译成功" -ForegroundColor Green
        } else {
            Write-Host "✗ 项目编译失败" -ForegroundColor Red
            Write-Host $result -ForegroundColor Red
            $script:ErrorCount++
        }
    } catch {
        Write-Host "✗ 编译过程中出现异常: $($_.Exception.Message)" -ForegroundColor Red
        $script:ErrorCount++
    }
} elseif ($dotnetPath) {
    Write-Host "找到 .NET CLI，尝试编译..." -ForegroundColor Cyan
    try {
        $result = & dotnet build "Emby.Plugins.JavScraper\Emby.Plugins.JavScraper.csproj" --configuration Release --verbosity quiet 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓ 项目编译成功" -ForegroundColor Green
        } else {
            Write-Host "✗ 项目编译失败" -ForegroundColor Red
            Write-Host $result -ForegroundColor Red
            $script:ErrorCount++
        }
    } catch {
        Write-Host "✗ 编译过程中出现异常: $($_.Exception.Message)" -ForegroundColor Red
        $script:ErrorCount++
    }
} else {
    Write-Host "⚠ 未找到 MSBuild 或 .NET CLI，跳过编译检查" -ForegroundColor Yellow
    $script:WarningCount++
}

Write-Host ""

# 总结
Write-Host "=== 验证结果 ===" -ForegroundColor Green
Write-Host "错误数量: $ErrorCount" -ForegroundColor $(if ($ErrorCount -eq 0) { "Green" } else { "Red" })
Write-Host "警告数量: $WarningCount" -ForegroundColor $(if ($WarningCount -eq 0) { "Green" } else { "Yellow" })

if ($ErrorCount -eq 0) {
    Write-Host ""
    Write-Host "🎉 项目验证通过！所有更新都已正确应用。" -ForegroundColor Green
    Write-Host ""
    Write-Host "下一步建议：" -ForegroundColor Cyan
    Write-Host "1. 在 Emby 服务器中测试插件功能"
    Write-Host "2. 验证 JavBus 和 JavDB 刮削器工作正常"
    Write-Host "3. 检查插件配置页面显示正确"
} else {
    Write-Host ""
    Write-Host "❌ 发现 $ErrorCount 个问题需要修复" -ForegroundColor Red
    exit 1
}
