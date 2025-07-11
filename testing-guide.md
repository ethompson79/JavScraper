# JavScraper 项目更新测试指南

## ✅ 验证结果

项目验证脚本已通过！所有更新都已正确应用：

- ✅ 所有 Jellyfin 相关文件已删除
- ✅ 所有条件编译指令已清理
- ✅ 依赖包已更新到最新版本
- ✅ 只保留 JavBus 和 JavDB 刮削器
- ✅ 项目配置已精简

## 🚀 实时开发和验证方法

### 方法一：实时验证（推荐）

#### 1. 快速语法和编译检查
```powershell
# 实时监控文件变化，自动进行语法检查
.\real-time-validation.ps1 -Watch

# 快速编译检查（无需完整构建）
.\real-time-validation.ps1 -Quick

# 只检查语法
.\real-time-validation.ps1 -Syntax

# 检查单个文件
.\real-time-validation.ps1 -TestFile "Emby.Plugins.JavScraper\Plugin.cs"
```

#### 2. 开发环境热重载设置
```powershell
# 设置符号链接，支持热重载（需要管理员权限）
.\dev-setup.ps1 -SetupSymlink

# 启动文件监控和自动编译
.\dev-setup.ps1 -StartWatcher

# 配置 VS Code 开发环境
.\dev-setup.ps1 -SetupVSCode
```

#### 3. 独立测试刮削器功能
```powershell
# 测试所有刮削器
.\test-scrapers.ps1 -TestId "SSIS-001"

# 只测试 JavBus
.\test-scrapers.ps1 -TestId "SSIS-001" -Scraper "JavBus"

# 详细输出并保存结果
.\test-scrapers.ps1 -TestId "SSIS-001" -Verbose -SaveResults
```

### 方法二：传统测试方法

#### 1. 编译测试

#### 方法一：使用 Visual Studio
1. 打开 `Emby.Plugins.JavScraper.sln`
2. 选择 `Release` 配置
3. 右键解决方案 → "重新生成解决方案"
4. 检查是否有编译错误

#### 方法二：使用 MSBuild（命令行）
```cmd
msbuild Emby.Plugins.JavScraper.sln /p:Configuration=Release
```

#### 方法三：使用 .NET CLI
```cmd
dotnet build Emby.Plugins.JavScraper/Emby.Plugins.JavScraper.csproj --configuration Release
```

## 🔄 推荐的开发工作流

### 日常开发流程
1. **初始设置**（只需一次）：
   ```powershell
   # 设置开发环境
   .\dev-setup.ps1 -SetupSymlink -EmbyPath "C:\path\to\emby\plugins"
   .\dev-setup.ps1 -SetupVSCode
   ```

2. **开始开发**：
   ```powershell
   # 终端1：启动文件监控和自动编译
   .\dev-setup.ps1 -StartWatcher

   # 终端2：启动实时验证
   .\real-time-validation.ps1 -Watch
   ```

3. **修改代码**：
   - 保存文件后自动编译
   - 自动进行语法检查
   - Emby 自动重载插件（如果设置了符号链接）

4. **功能测试**：
   ```powershell
   # 测试特定功能
   .\test-scrapers.ps1 -TestId "SSIS-001" -Scraper "JavBus"
   ```

5. **最终验证**：
   ```powershell
   # 完整验证
   .\verify-project.ps1
   ```

### 无需重启 Emby 的开发方式

#### 使用符号链接热重载
1. 以管理员身份运行：
   ```powershell
   .\dev-setup.ps1 -SetupSymlink
   ```

2. 修改代码后，插件会自动重新加载，无需重启 Emby

#### 优势
- ⚡ 即时看到修改效果
- 🔄 无需手动复制文件
- 🚫 无需重启 Emby 服务
- 📊 实时编译状态反馈

### 2. 传统 Emby 测试方法

#### 步骤 1：安装插件
1. 编译成功后，在 `Emby.Plugins.JavScraper/bin/Release/` 目录找到 `JavScraper.dll`
2. 将 `JavScraper.dll` 复制到 Emby 插件目录：
   - Windows: `%AppData%\Emby-Server\programdata\plugins\`
   - Linux: `/var/lib/emby/plugins/`
   - Docker: `/config/plugins/`
3. 重启 Emby Server

#### 步骤 2：验证插件加载
1. 登录 Emby 管理界面
2. 进入 "服务器" → "插件"
3. 确认 "JavScraper" 插件出现在列表中且状态为"活动"

#### 步骤 3：检查配置页面
1. 点击 JavScraper 插件进入配置
2. 验证配置页面正常显示
3. 检查 "刮削器" 部分只显示 JavBus 和 JavDB 两个选项
4. 确认没有其他数据源（如 FC2、AVSOX 等）

#### 步骤 4：测试刮削功能
1. 创建一个电影媒体库
2. 在媒体库设置中：
   - 媒体库类型选择 "电影"
   - 显示高级设置
   - 在 "Movie元数据下载器" 中只勾选 "JavScraper"
   - 在 "Movie图片获取程序" 中只勾选 "JavScraper"
3. 添加测试视频文件（文件名包含番号，如 "SSIS-001.mp4"）
4. 扫描媒体库
5. 检查是否能正确获取元数据和封面图片

### 3. 功能验证清单

#### 核心功能 ✅
- [ ] 番号识别功能正常
- [ ] JavBus 刮削器工作正常
- [ ] JavDB 刮削器工作正常
- [ ] 元数据获取（标题、演员、类型等）
- [ ] 封面图片下载
- [ ] 女优头像采集任务可执行

#### 配置功能 ✅
- [ ] 插件配置页面显示正常
- [ ] 代理设置功能正常
- [ ] 刮削器优先级设置
- [ ] 翻译功能（如果启用百度翻译）

#### 性能验证 ✅
- [ ] 插件启动速度更快（移除了多余刮削器）
- [ ] 内存占用减少
- [ ] 无 Jellyfin 相关错误日志

### 4. 日志检查

#### 查看 Emby 日志
- Windows: `%AppData%\Emby-Server\logs\`
- Linux: `/var/lib/emby/logs/`

#### 关键日志信息
1. **插件加载成功**：
   ```
   JavScraper - Loaded.
   ```

2. **无 Jellyfin 错误**：
   - 不应该有任何包含 "Jellyfin" 的错误信息

3. **刮削器初始化**：
   - 应该只看到 JavBus 和 JavDB 的初始化信息

### 5. 故障排除

#### 常见问题
1. **编译错误**：
   - 检查 .NET Framework 版本
   - 确认所有 NuGet 包已正确还原

2. **插件加载失败**：
   - 检查 Emby 版本兼容性（需要 4.7+ 版本）
   - 验证插件文件权限

3. **刮削失败**：
   - 检查网络连接
   - 验证代理设置（如果使用）
   - 确认 JavBus/JavDB 网站可访问

#### 调试建议
1. 启用 Emby 详细日志记录
2. 检查插件目录权限
3. 验证依赖库版本匹配

### 6. 性能对比

#### 更新前 vs 更新后
- **刮削器数量**：7个 → 2个
- **代码行数**：减少约30%
- **编译时间**：更快
- **运行时内存**：更少
- **维护复杂度**：显著降低

## 🎉 预期结果

如果所有测试都通过，您应该看到：

1. ✅ 项目编译无错误
2. ✅ 插件在 Emby 中正常加载
3. ✅ 配置页面只显示 JavBus 和 JavDB
4. ✅ 刮削功能正常工作
5. ✅ 无 Jellyfin 相关错误
6. ✅ 性能有所提升

## 📞 技术支持

如果遇到问题：
1. 检查 Emby 日志文件
2. 确认 Emby 版本兼容性
3. 验证网络连接和代理设置
4. 重新编译和安装插件

---

**恭喜！** 您的 JavScraper 项目已成功更新到最新的 Emby API，并精简为只支持 JavBus 和 JavDB 数据源。
