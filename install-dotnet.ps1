# .NET CLI 自动安装脚本
# 自动检测并安装适合的 .NET SDK 版本

param(
    [string]$Version = "8.0",  # 默认安装 .NET 8.0
    [switch]$Portable,         # 便携版安装（无需管理员权限）
    [string]$InstallPath = "C:\dotnet-portable",
    [switch]$Force             # 强制重新安装
)

Write-Host "=== .NET CLI 自动安装脚本 ===" -ForegroundColor Green
Write-Host ""

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

function Test-DotNetInstalled {
    try {
        $version = & dotnet --version 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Status "检测到已安装的 .NET SDK: $version" "Success"
            
            # 检查版本是否满足要求
            $majorVersion = [int]($version.Split('.')[0])
            if ($majorVersion -ge 6) {
                Write-Status ".NET SDK 版本满足要求 (>= 6.0)" "Success"
                return $true
            } else {
                Write-Status ".NET SDK 版本过低，需要升级" "Warning"
                return $false
            }
        }
    }
    catch {
        Write-Status "未检测到 .NET SDK" "Info"
        return $false
    }
    return $false
}

function Test-AdminRights {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Install-DotNetPortable {
    param($Version, $InstallPath)
    
    Write-Status "开始便携版安装..." "Info"
    
    try {
        # 创建安装目录
        if (-not (Test-Path $InstallPath)) {
            New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
            Write-Status "创建安装目录: $InstallPath" "Info"
        }
        
        # 下载安装脚本
        $scriptPath = Join-Path $InstallPath "dotnet-install.ps1"
        Write-Status "下载安装脚本..." "Info"
        
        Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $scriptPath -UseBasicParsing
        
        # 执行安装
        Write-Status "安装 .NET $Version SDK..." "Info"
        & $scriptPath -Channel $Version -InstallDir $InstallPath -NoPath
        
        if ($LASTEXITCODE -eq 0) {
            Write-Status ".NET SDK 安装成功" "Success"
            
            # 添加到环境变量
            $currentPath = [Environment]::GetEnvironmentVariable("PATH", "User")
            if ($currentPath -notlike "*$InstallPath*") {
                Write-Status "添加到用户环境变量..." "Info"
                [Environment]::SetEnvironmentVariable("PATH", "$InstallPath;$currentPath", "User")
                
                # 临时添加到当前会话
                $env:PATH = "$InstallPath;$env:PATH"
                
                Write-Status "环境变量已更新，重启终端后生效" "Warning"
            }
            
            return $true
        } else {
            Write-Status "安装失败" "Error"
            return $false
        }
    }
    catch {
        Write-Status "安装异常: $($_.Exception.Message)" "Error"
        return $false
    }
}

function Install-DotNetWinget {
    param($Version)
    
    Write-Status "尝试使用 Winget 安装..." "Info"
    
    # 检查 Winget 是否可用
    $wingetPath = Get-Command "winget" -ErrorAction SilentlyContinue
    if (-not $wingetPath) {
        Write-Status "Winget 不可用" "Warning"
        return $false
    }
    
    try {
        $packageName = "Microsoft.DotNet.SDK.$Version"
        Write-Status "安装包: $packageName" "Info"
        
        & winget install $packageName --accept-source-agreements --accept-package-agreements
        
        if ($LASTEXITCODE -eq 0) {
            Write-Status "Winget 安装成功" "Success"
            return $true
        } else {
            Write-Status "Winget 安装失败" "Warning"
            return $false
        }
    }
    catch {
        Write-Status "Winget 安装异常: $($_.Exception.Message)" "Warning"
        return $false
    }
}

function Install-DotNetOfficial {
    Write-Status "请手动安装 .NET SDK:" "Warning"
    Write-Status "1. 访问: https://dotnet.microsoft.com/download" "Info"
    Write-Status "2. 下载 .NET $Version SDK" "Info"
    Write-Status "3. 运行安装程序" "Info"
    Write-Status "4. 重启终端" "Info"
    
    # 尝试打开下载页面
    try {
        Start-Process "https://dotnet.microsoft.com/download"
        Write-Status "已打开官方下载页面" "Info"
    }
    catch {
        Write-Status "无法打开浏览器，请手动访问下载页面" "Warning"
    }
    
    return $false
}

function Test-JavScraperProject {
    Write-Status "测试 JavScraper 项目编译..." "Info"
    
    $projectPath = "Emby.Plugins.JavScraper\Emby.Plugins.JavScraper.csproj"
    if (-not (Test-Path $projectPath)) {
        Write-Status "未找到 JavScraper 项目文件" "Warning"
        return $false
    }
    
    try {
        # 恢复依赖
        Write-Status "恢复 NuGet 包..." "Info"
        & dotnet restore $projectPath --verbosity quiet
        
        if ($LASTEXITCODE -ne 0) {
            Write-Status "依赖恢复失败" "Error"
            return $false
        }
        
        # 编译项目
        Write-Status "编译项目..." "Info"
        & dotnet build $projectPath --configuration Debug --verbosity quiet
        
        if ($LASTEXITCODE -eq 0) {
            Write-Status "JavScraper 项目编译成功！" "Success"
            return $true
        } else {
            Write-Status "项目编译失败" "Error"
            return $false
        }
    }
    catch {
        Write-Status "测试编译异常: $($_.Exception.Message)" "Error"
        return $false
    }
}

# 主执行逻辑
Write-Status "检查当前 .NET 安装状态..." "Info"

if (-not $Force -and (Test-DotNetInstalled)) {
    Write-Status "已有合适的 .NET SDK，跳过安装" "Success"
    
    # 测试项目编译
    if (Test-JavScraperProject) {
        Write-Status "环境配置完成！可以开始开发了" "Success"
        exit 0
    } else {
        Write-Status "项目编译失败，可能需要重新安装 .NET SDK" "Warning"
    }
} else {
    Write-Status "需要安装 .NET SDK $Version" "Info"
}

$installSuccess = $false

if ($Portable) {
    Write-Status "使用便携版安装模式" "Info"
    $installSuccess = Install-DotNetPortable $Version $InstallPath
} else {
    # 检查管理员权限
    if (-not (Test-AdminRights)) {
        Write-Status "检测到非管理员权限，尝试 Winget 安装..." "Warning"
        $installSuccess = Install-DotNetWinget $Version
        
        if (-not $installSuccess) {
            Write-Status "Winget 安装失败，切换到便携版安装" "Warning"
            $installSuccess = Install-DotNetPortable $Version $InstallPath
        }
    } else {
        Write-Status "检测到管理员权限，尝试 Winget 安装..." "Info"
        $installSuccess = Install-DotNetWinget $Version
        
        if (-not $installSuccess) {
            Install-DotNetOfficial
        }
    }
}

# 验证安装结果
if ($installSuccess) {
    Write-Host ""
    Write-Status "=== 安装完成 ===" "Success"
    
    # 重新检查安装
    Start-Sleep -Seconds 2
    if (Test-DotNetInstalled) {
        Write-Status "✓ .NET SDK 安装验证成功" "Success"
        
        # 测试项目编译
        if (Test-JavScraperProject) {
            Write-Status "✓ JavScraper 项目编译测试成功" "Success"
            Write-Host ""
            Write-Status "🎉 环境配置完成！现在可以使用以下命令:" "Success"
            Write-Status "  .\quick-test.ps1           # 快速验证" "Info"
            Write-Status "  .\dev-setup.ps1 -StartWatcher  # 开发监控" "Info"
            Write-Status "  dotnet build Emby.Plugins.JavScraper\Emby.Plugins.JavScraper.csproj  # 手动编译" "Info"
        } else {
            Write-Status "⚠ 项目编译测试失败，可能需要手动解决依赖问题" "Warning"
        }
    } else {
        Write-Status "✗ 安装验证失败，请重启终端后重试" "Error"
    }
} else {
    Write-Host ""
    Write-Status "=== 安装失败 ===" "Error"
    Write-Status "请尝试以下解决方案:" "Info"
    Write-Status "1. 以管理员身份运行此脚本" "Info"
    Write-Status "2. 使用便携版安装: .\install-dotnet.ps1 -Portable" "Info"
    Write-Status "3. 手动从官网下载安装: https://dotnet.microsoft.com/download" "Info"
    exit 1
}
