# JavScraper 项目更新验证指南

## 1. 编译测试

### 使用 Visual Studio
1. 打开 `Emby.Plugins.JavScraper.sln`
2. 选择 `Release` 配置
3. 右键点击解决方案 → "重新生成解决方案"
4. 检查输出窗口是否有编译错误

### 使用命令行 (.NET SDK)
```bash
# 在项目根目录执行
dotnet build Emby.Plugins.JavScraper/Emby.Plugins.JavScraper.csproj --configuration Release
```

### 使用 MSBuild
```bash
# 在项目根目录执行
msbuild Emby.Plugins.JavScraper.sln /p:Configuration=Release
```

## 2. 项目结构验证

### 检查已删除的文件
确认以下文件/目录已被删除：
- [ ] `Jellyfin.GenerateConfigurationPage/` 目录
- [ ] `Emby.Plugins.JavScraper/Configuration/Jellyfin.ConfigPage.html`
- [ ] `Emby.Plugins.JavScraper/Configuration/Jellyfin.JavOrganizationConfigPage.html`
- [ ] `Emby.Plugins.JavScraper/Extensions/JellyfinExtensions.cs`
- [ ] `Emby.Plugins.JavScraper/Scrapers/AVSOX.cs`
- [ ] `Emby.Plugins.JavScraper/Scrapers/FC2.cs`
- [ ] `Emby.Plugins.JavScraper/Scrapers/Jav123.cs`
- [ ] `Emby.Plugins.JavScraper/Scrapers/MgsTage.cs`
- [ ] `Emby.Plugins.JavScraper/Scrapers/R18.cs`

### 检查保留的刮削器
确认以下文件存在：
- [ ] `Emby.Plugins.JavScraper/Scrapers/JavBus.cs`
- [ ] `Emby.Plugins.JavScraper/Scrapers/JavDB.cs`
- [ ] `Emby.Plugins.JavScraper/Scrapers/Gfriends.cs`

## 3. 代码验证

### 检查条件编译指令
在项目中搜索 `__JELLYFIN__`，应该没有任何结果。

### 检查依赖版本
打开 `Emby.Plugins.JavScraper/Emby.Plugins.JavScraper.csproj`，确认：
- [ ] `MediaBrowser.Server.Core` 版本为 `4.8.11`
- [ ] `HtmlAgilityPack` 版本为 `1.11.70`
- [ ] `LiteDB` 版本为 `5.0.21`
- [ ] `SkiaSharp` 版本为 `2.88.8`
- [ ] 没有 `Jellyfin.Controller` 引用

## 4. 功能测试

### 在 Emby 中测试
1. **编译插件**：
   - 编译成功后，在 `bin/Release/` 目录找到 `JavScraper.dll`

2. **安装插件**：
   - 将 `JavScraper.dll` 复制到 Emby 插件目录
   - 重启 Emby Server

3. **验证插件加载**：
   - 登录 Emby 管理界面
   - 进入 "服务器" → "插件"
   - 确认 "JavScraper" 插件出现在列表中

4. **验证配置页面**：
   - 点击 JavScraper 插件
   - 确认配置页面正常显示
   - 检查只有 JavBus 和 JavDB 两个数据源

5. **测试刮削功能**：
   - 创建一个电影媒体库
   - 在媒体库设置中启用 JavScraper 元数据下载器
   - 添加一个测试视频文件（文件名包含番号，如 "SSIS-001.mp4"）
   - 扫描媒体库，检查是否能正确获取元数据

## 5. 日志检查

### Emby 日志位置
- Windows: `%AppData%\Emby-Server\logs\`
- Linux: `/var/lib/emby/logs/`

### 检查要点
1. **插件加载日志**：
   ```
   JavScraper - Loaded.
   ```

2. **无 Jellyfin 相关错误**：
   - 不应该有任何 Jellyfin 相关的错误信息

3. **刮削器初始化**：
   - 应该只看到 JavBus 和 JavDB 的初始化信息

## 6. 性能验证

### 内存使用
- 由于移除了多个刮削器，插件内存占用应该有所减少

### 启动时间
- 插件加载时间应该更快（减少了条件编译和多余的刮削器初始化）

## 7. 兼容性测试

### Emby 版本兼容性
测试插件在以下 Emby 版本中的兼容性：
- [ ] Emby Server 4.8.x
- [ ] Emby Server 4.7.x（如果需要向后兼容）

### 操作系统兼容性
- [ ] Windows
- [ ] Linux
- [ ] macOS（如果适用）

## 8. 回归测试

### 核心功能验证
- [ ] 番号识别功能正常
- [ ] 元数据获取功能正常
- [ ] 图片下载功能正常
- [ ] 女优信息采集功能正常
- [ ] 翻译功能正常（如果启用）
- [ ] 代理功能正常（如果配置）

### 配置功能验证
- [ ] 插件配置保存/加载正常
- [ ] 刮削器优先级设置正常
- [ ] 代理设置功能正常

## 故障排除

### 常见问题
1. **编译错误**：检查 .NET Framework/Core 版本
2. **插件加载失败**：检查 Emby 版本兼容性
3. **刮削失败**：检查网络连接和代理设置
4. **配置丢失**：检查配置文件权限

### 调试建议
1. 启用详细日志记录
2. 使用 Emby 的调试模式
3. 检查插件目录权限
4. 验证依赖库版本
