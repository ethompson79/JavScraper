# JavScraper 开发环境快速设置脚本
# 自动化设置开发环境，支持热重载和实时验证

param(
    [switch]$SetupSymlink,
    [switch]$StartWatcher,
    [switch]$SetupVSCode,
    [string]$EmbyPath = ""
)

Write-Host "=== JavScraper 开发环境设置 ===" -ForegroundColor Green
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

function Test-AdminRights {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Setup-SymbolicLink {
    param($EmbyPluginPath)
    
    Write-Status "设置符号链接以支持热重载..." "Info"
    
    if (-not (Test-AdminRights)) {
        Write-Status "❌ 需要管理员权限来创建符号链接" "Error"
        Write-Status "请以管理员身份运行此脚本" "Error"
        return $false
    }
    
    $sourceDll = "Emby.Plugins.JavScraper\bin\Debug\JavScraper.dll"
    $targetDll = Join-Path $EmbyPluginPath "JavScraper.dll"
    
    if (-not (Test-Path $sourceDll)) {
        Write-Status "源文件不存在，先编译项目..." "Warning"
        try {
            & dotnet build "Emby.Plugins.JavScraper\Emby.Plugins.JavScraper.csproj" --configuration Debug
            if ($LASTEXITCODE -ne 0) {
                Write-Status "编译失败" "Error"
                return $false
            }
        }
        catch {
            Write-Status "编译异常: $($_.Exception.Message)" "Error"
            return $false
        }
    }
    
    # 创建符号链接
    try {
        if (Test-Path $targetDll) {
            Remove-Item $targetDll -Force
        }
        
        New-Item -ItemType SymbolicLink -Path $targetDll -Target (Resolve-Path $sourceDll).Path
        Write-Status "✓ 符号链接创建成功" "Success"
        Write-Status "  源文件: $sourceDll" "Info"
        Write-Status "  目标位置: $targetDll" "Info"
        return $true
    }
    catch {
        Write-Status "创建符号链接失败: $($_.Exception.Message)" "Error"
        return $false
    }
}

function Start-DevelopmentWatcher {
    Write-Status "启动开发监控器..." "Info"
    
    # 创建多个监控任务
    $jobs = @()
    
    # 1. 文件变化监控和自动编译
    $compileJob = Start-Job -ScriptBlock {
        param($ProjectPath)
        
        $watcher = New-Object System.IO.FileSystemWatcher
        $watcher.Path = $ProjectPath
        $watcher.Filter = "*.cs"
        $watcher.IncludeSubdirectories = $true
        $watcher.EnableRaisingEvents = $true
        
        $lastCompile = Get-Date
        
        $action = {
            $path = $Event.SourceEventArgs.FullPath
            $fileName = Split-Path $path -Leaf
            
            # 避免频繁编译
            if ((Get-Date) - $script:lastCompile -lt [TimeSpan]::FromSeconds(2)) {
                return
            }
            
            Write-Host "[$((Get-Date).ToString('HH:mm:ss'))] 检测到 $fileName 变化，开始编译..." -ForegroundColor Cyan
            
            # 等待文件写入完成
            Start-Sleep -Milliseconds 1000
            
            try {
                $result = & dotnet build "$ProjectPath\Emby.Plugins.JavScraper.csproj" --configuration Debug --verbosity quiet 2>&1
                if ($LASTEXITCODE -eq 0) {
                    Write-Host "[$((Get-Date).ToString('HH:mm:ss'))] ✓ 编译成功" -ForegroundColor Green
                } else {
                    Write-Host "[$((Get-Date).ToString('HH:mm:ss'))] ✗ 编译失败:" -ForegroundColor Red
                    $result | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
                }
            }
            catch {
                Write-Host "[$((Get-Date).ToString('HH:mm:ss'))] ✗ 编译异常: $($_.Exception.Message)" -ForegroundColor Red
            }
            
            $script:lastCompile = Get-Date
        }
        
        Register-ObjectEvent -InputObject $watcher -EventName "Changed" -Action $action
        
        Write-Host "文件监控已启动，按任意键停止..." -ForegroundColor Yellow
        
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
    } -ArgumentList (Resolve-Path "Emby.Plugins.JavScraper").Path
    
    $jobs += $compileJob
    
    # 2. 实时验证监控
    $validationJob = Start-Job -ScriptBlock {
        param($ScriptPath)
        
        while ($true) {
            Start-Sleep -Seconds 10
            
            try {
                & powershell -ExecutionPolicy Bypass -File "$ScriptPath\real-time-validation.ps1" -Syntax -ErrorAction SilentlyContinue
            }
            catch {
                # 忽略验证错误，避免干扰主要工作流
            }
        }
    } -ArgumentList (Get-Location).Path
    
    $jobs += $validationJob
    
    Write-Status "开发监控器已启动" "Success"
    Write-Status "- 文件变化自动编译" "Info"
    Write-Status "- 定期语法验证" "Info"
    Write-Status "按 Ctrl+C 停止监控" "Warning"
    
    try {
        # 等待用户中断
        while ($true) {
            Start-Sleep -Seconds 1
            
            # 检查作业状态
            foreach ($job in $jobs) {
                if ($job.State -eq "Failed") {
                    Write-Status "监控作业失败，重新启动..." "Warning"
                    Remove-Job $job -Force
                }
            }
        }
    }
    finally {
        Write-Status "停止所有监控作业..." "Info"
        $jobs | ForEach-Object { 
            Stop-Job $_ -ErrorAction SilentlyContinue
            Remove-Job $_ -Force -ErrorAction SilentlyContinue
        }
    }
}

function Setup-VSCodeConfig {
    Write-Status "设置 VS Code 配置..." "Info"
    
    $vscodeDir = ".vscode"
    if (-not (Test-Path $vscodeDir)) {
        New-Item -ItemType Directory -Path $vscodeDir | Out-Null
    }
    
    # 创建 tasks.json
    $tasksJson = @{
        version = "2.0.0"
        tasks = @(
            @{
                label = "build"
                command = "dotnet"
                type = "process"
                args = @("build", "Emby.Plugins.JavScraper/Emby.Plugins.JavScraper.csproj", "--configuration", "Debug")
                group = @{
                    kind = "build"
                    isDefault = $true
                }
                presentation = @{
                    echo = $true
                    reveal = "silent"
                    focus = $false
                    panel = "shared"
                    showReuseMessage = $true
                    clear = $false
                }
                problemMatcher = "`$msCompile"
            },
            @{
                label = "quick-validate"
                command = "powershell"
                type = "process"
                args = @("-ExecutionPolicy", "Bypass", "-File", "real-time-validation.ps1", "-Quick")
                group = "test"
                presentation = @{
                    echo = $true
                    reveal = "always"
                    focus = $false
                    panel = "new"
                }
            },
            @{
                label = "watch-files"
                command = "powershell"
                type = "process"
                args = @("-ExecutionPolicy", "Bypass", "-File", "real-time-validation.ps1", "-Watch")
                group = "test"
                isBackground = $true
                presentation = @{
                    echo = $true
                    reveal = "always"
                    focus = $false
                    panel = "new"
                }
            }
        )
    }
    
    $tasksJson | ConvertTo-Json -Depth 10 | Out-File "$vscodeDir/tasks.json" -Encoding UTF8
    
    # 创建 launch.json（用于调试配置）
    $launchJson = @{
        version = "0.2.0"
        configurations = @(
            @{
                name = "Attach to Emby"
                type = "coreclr"
                request = "attach"
                processName = "EmbyServer"
            }
        )
    }
    
    $launchJson | ConvertTo-Json -Depth 10 | Out-File "$vscodeDir/launch.json" -Encoding UTF8
    
    # 创建 settings.json
    $settingsJson = @{
        "files.exclude" = @{
            "**/bin" = $true
            "**/obj" = $true
        }
        "dotnet.completion.showCompletionItemsFromUnimportedNamespaces" = $true
        "omnisharp.enableRoslynAnalyzers" = $true
    }
    
    $settingsJson | ConvertTo-Json -Depth 10 | Out-File "$vscodeDir/settings.json" -Encoding UTF8
    
    Write-Status "✓ VS Code 配置已创建" "Success"
    Write-Status "  - 构建任务 (Ctrl+Shift+P -> Tasks: Run Task -> build)" "Info"
    Write-Status "  - 快速验证 (Ctrl+Shift+P -> Tasks: Run Task -> quick-validate)" "Info"
    Write-Status "  - 文件监控 (Ctrl+Shift+P -> Tasks: Run Task -> watch-files)" "Info"
}

function Find-EmbyPath {
    $commonPaths = @(
        "$env:APPDATA\Emby-Server\programdata\plugins",
        "$env:PROGRAMDATA\Emby-Server\programdata\plugins",
        "C:\ProgramData\Emby-Server\programdata\plugins",
        "D:\emby\programdata\plugins"
    )
    
    foreach ($path in $commonPaths) {
        if (Test-Path $path) {
            return $path
        }
    }
    
    return $null
}

# 主执行逻辑
if ($SetupSymlink) {
    if ($EmbyPath -eq "") {
        $EmbyPath = Find-EmbyPath
        if ($EmbyPath -eq $null) {
            Write-Status "未找到 Emby 插件目录，请手动指定 -EmbyPath 参数" "Error"
            exit 1
        }
        Write-Status "自动检测到 Emby 插件目录: $EmbyPath" "Info"
    }
    
    if (-not (Test-Path $EmbyPath)) {
        Write-Status "Emby 插件目录不存在: $EmbyPath" "Error"
        exit 1
    }
    
    Setup-SymbolicLink $EmbyPath
}
elseif ($StartWatcher) {
    Start-DevelopmentWatcher
}
elseif ($SetupVSCode) {
    Setup-VSCodeConfig
}
else {
    Write-Status "JavScraper 开发环境设置选项:" "Info"
    Write-Status ""
    Write-Status "快速开发设置 (推荐):" "Success"
    Write-Status "  .\dev-setup.ps1 -SetupSymlink    # 设置符号链接，支持热重载" "Info"
    Write-Status "  .\dev-setup.ps1 -StartWatcher    # 启动文件监控和自动编译" "Info"
    Write-Status "  .\dev-setup.ps1 -SetupVSCode     # 配置 VS Code 开发环境" "Info"
    Write-Status ""
    Write-Status "实时验证:" "Success"
    Write-Status "  .\real-time-validation.ps1 -Watch    # 监控文件变化" "Info"
    Write-Status "  .\real-time-validation.ps1 -Quick    # 快速编译检查" "Info"
    Write-Status "  .\real-time-validation.ps1 -Syntax   # 语法检查" "Info"
    Write-Status ""
    Write-Status "推荐的开发工作流:" "Warning"
    Write-Status "1. 运行 .\dev-setup.ps1 -SetupSymlink 设置热重载" "Info"
    Write-Status "2. 运行 .\dev-setup.ps1 -StartWatcher 启动自动编译" "Info"
    Write-Status "3. 在另一个终端运行 .\real-time-validation.ps1 -Watch" "Info"
    Write-Status "4. 修改代码后自动编译，Emby 自动重载插件" "Info"
}
