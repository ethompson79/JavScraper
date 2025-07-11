# 🚀 JavScraper 开发环境快速设置

## 📋 一键安装 .NET CLI

### 方法一：自动安装脚本（推荐）

```powershell
# 自动检测并安装 .NET SDK
.\install-dotnet.ps1

# 如果没有管理员权限，使用便携版
.\install-dotnet.ps1 -Portable

# 强制重新安装
.\install-dotnet.ps1 -Force
```

### 方法二：手动安装（最简单）

#### Windows 10/11 用户
1. 按 `Win + R`，输入 `ms-windows-store://pdp/?productid=9NBLGGH4NNS1`
2. 或者访问：https://dotnet.microsoft.com/download
3. 下载并安装 **.NET 8.0 SDK**
4. 重启 PowerShell

#### 使用 Winget（推荐）
```powershell
winget install Microsoft.DotNet.SDK.8
```

#### 使用 Chocolatey
```powershell
choco install dotnet-sdk
```

## ✅ 验证安装

安装完成后，运行以下命令验证：

```powershell
# 检查版本
dotnet --version

# 应该显示类似：8.0.100
```

## 🎯 完整开发环境设置

### 步骤1：安装 .NET CLI
```powershell
.\install-dotnet.ps1
```

### 步骤2：验证项目
```powershell
.\quick-test.ps1
```

### 步骤3：设置开发环境（可选）
```powershell
# 设置热重载（需要管理员权限）
.\dev-setup.ps1 -SetupSymlink

# 启动开发监控
.\dev-setup.ps1 -StartWatcher
```

## 🔧 常见问题解决

### 问题1：权限不足
```
Access denied
```
**解决方案：**
```powershell
# 使用便携版安装
.\install-dotnet.ps1 -Portable
```

### 问题2：网络问题
```
Unable to download
```
**解决方案：**
1. 检查网络连接
2. 使用手动安装方式
3. 配置代理（如果需要）

### 问题3：命令未找到
```
'dotnet' is not recognized
```
**解决方案：**
1. 重启 PowerShell
2. 检查环境变量
3. 重新安装

## 📊 安装选项对比

| 方法 | 优点 | 缺点 | 推荐度 |
|------|------|------|--------|
| 自动脚本 | 全自动，智能选择 | 需要网络 | ⭐⭐⭐⭐⭐ |
| Winget | 官方包管理器 | 需要 Windows 10+ | ⭐⭐⭐⭐ |
| 官方安装程序 | 最稳定 | 需要手动下载 | ⭐⭐⭐ |
| 便携版 | 无需管理员权限 | 需要手动配置环境变量 | ⭐⭐⭐ |

## 🎉 安装完成后

安装成功后，您就可以：

### 1. 快速验证项目
```powershell
.\quick-test.ps1
```
预期输出：
```
=== JavScraper Quick Test ===
[PASS] Required file: Emby.Plugins.JavScraper\Plugin.cs
[PASS] Basic syntax checks
[PASS] Project compilation
SUCCESS: All tests passed!
```

### 2. 开始开发
```powershell
# 启动文件监控
.\dev-setup.ps1 -StartWatcher

# 修改代码，自动编译！
```

### 3. 测试功能
```powershell
# 测试刮削器
.\test-scrapers.ps1 -TestId "SSIS-001"
```

## 🛠️ 开发工具推荐

### Visual Studio Code（推荐）
```powershell
# 安装 VS Code
winget install Microsoft.VisualStudioCode

# 配置开发环境
.\dev-setup.ps1 -SetupVSCode
```

### Visual Studio Community（功能最全）
```powershell
winget install Microsoft.VisualStudio.2022.Community
```

## 📚 下一步

1. ✅ 安装 .NET CLI
2. ✅ 运行 `.\quick-test.ps1` 验证
3. ✅ 阅读 `DEVELOPMENT-GUIDE.md` 了解开发流程
4. ✅ 开始修改代码并实时验证！

---

**需要帮助？** 查看详细文档：
- `install-dotnet-cli.md` - 详细安装指南
- `DEVELOPMENT-GUIDE.md` - 完整开发指南
- `testing-guide.md` - 测试指南
