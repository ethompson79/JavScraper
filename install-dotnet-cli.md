# .NET CLI 安装指南

## 🎯 为什么需要 .NET CLI

.NET CLI 是开发 JavScraper 插件必需的工具，用于：
- ✅ 编译项目
- ✅ 管理依赖包
- ✅ 运行测试
- ✅ 发布应用

## 📋 系统要求

- Windows 10 版本 1607 或更高版本
- Windows Server 2012 R2 或更高版本
- 至少 500 MB 可用磁盘空间

## 🚀 安装方法

### 方法一：官方安装程序（推荐）

#### 1. 下载安装程序
访问官方下载页面：https://dotnet.microsoft.com/download

#### 2. 选择版本
- **推荐**：.NET 8.0 (LTS) - 长期支持版本
- **最低要求**：.NET 6.0 - 支持 JavScraper 项目

#### 3. 下载对应版本
- **x64 系统**：下载 x64 版本
- **x86 系统**：下载 x86 版本
- **ARM64 系统**：下载 ARM64 版本

#### 4. 运行安装程序
- 双击下载的 `.exe` 文件
- 按照向导完成安装
- 重启命令提示符或 PowerShell

### 方法二：使用 Winget（Windows 10/11）

```powershell
# 安装最新版本
winget install Microsoft.DotNet.SDK.8

# 或安装 .NET 6
winget install Microsoft.DotNet.SDK.6
```

### 方法三：使用 Chocolatey

```powershell
# 安装 Chocolatey（如果没有）
Set-ExecutionPolicy Bypass -Scope Process -Force
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072
iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))

# 安装 .NET SDK
choco install dotnet-sdk
```

### 方法四：便携版本（无需管理员权限）

#### 1. 下载便携版
```powershell
# 创建目录
mkdir C:\dotnet
cd C:\dotnet

# 下载安装脚本
Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile "dotnet-install.ps1"

# 安装 .NET 8.0
.\dotnet-install.ps1 -Channel 8.0 -InstallDir "C:\dotnet"
```

#### 2. 添加到环境变量
```powershell
# 临时添加到当前会话
$env:PATH = "C:\dotnet;$env:PATH"

# 永久添加（需要重启终端）
[Environment]::SetEnvironmentVariable("PATH", "C:\dotnet;$env:PATH", "User")
```

## ✅ 验证安装

### 检查安装是否成功
```powershell
# 检查 .NET 版本
dotnet --version

# 查看所有已安装的 SDK
dotnet --list-sdks

# 查看所有已安装的运行时
dotnet --list-runtimes
```

### 预期输出示例
```
PS> dotnet --version
8.0.100

PS> dotnet --list-sdks
6.0.416 [C:\Program Files\dotnet\sdk]
8.0.100 [C:\Program Files\dotnet\sdk]

PS> dotnet --list-runtimes
Microsoft.AspNetCore.App 6.0.24 [C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App]
Microsoft.NETCore.App 6.0.24 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
Microsoft.AspNetCore.App 8.0.0 [C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App]
Microsoft.NETCore.App 8.0.0 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
```

## 🔧 测试 JavScraper 项目

安装完成后，测试是否能编译 JavScraper：

```powershell
# 进入项目目录
cd "G:\我的云端硬盘\Jav\JavScraper"

# 恢复依赖包
dotnet restore Emby.Plugins.JavScraper/Emby.Plugins.JavScraper.csproj

# 编译项目
dotnet build Emby.Plugins.JavScraper/Emby.Plugins.JavScraper.csproj --configuration Release

# 运行快速测试
.\quick-test.ps1
```

## 🛠️ 故障排除

### 问题1：命令未找到
```
'dotnet' 不是内部或外部命令，也不是可运行的程序或批处理文件。
```

**解决方案：**
1. 重启命令提示符或 PowerShell
2. 检查环境变量 PATH 是否包含 .NET 安装路径
3. 重新安装 .NET SDK

### 问题2：权限不足
```
Access to the path 'C:\Program Files\dotnet' is denied.
```

**解决方案：**
1. 以管理员身份运行安装程序
2. 或使用便携版本安装到用户目录

### 问题3：网络连接问题
```
Unable to download package from 'https://api.nuget.org/v3-flatcontainer/...'
```

**解决方案：**
```powershell
# 配置 NuGet 源
dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org

# 清除缓存
dotnet nuget locals all --clear

# 重新恢复包
dotnet restore
```

### 问题4：版本兼容性
如果项目需要特定版本的 .NET，可以安装多个版本：

```powershell
# 安装 .NET 6.0（JavScraper 最低要求）
winget install Microsoft.DotNet.SDK.6

# 安装 .NET 8.0（推荐）
winget install Microsoft.DotNet.SDK.8
```

## 📚 有用的 .NET CLI 命令

### 项目管理
```powershell
# 创建新项目
dotnet new classlib -n MyProject

# 添加包引用
dotnet add package PackageName

# 移除包引用
dotnet remove package PackageName

# 列出包引用
dotnet list package
```

### 构建和运行
```powershell
# 清理项目
dotnet clean

# 恢复依赖
dotnet restore

# 编译项目
dotnet build

# 发布项目
dotnet publish -c Release
```

## 🎉 安装完成后

安装成功后，您就可以：

1. **使用完整的验证脚本**：
   ```powershell
   .\quick-test.ps1  # 现在包含编译测试
   ```

2. **设置开发环境**：
   ```powershell
   .\dev-setup.ps1 -StartWatcher  # 自动编译监控
   ```

3. **手动编译项目**：
   ```powershell
   dotnet build Emby.Plugins.JavScraper/Emby.Plugins.JavScraper.csproj
   ```

现在您的开发环境就完整了！🚀
