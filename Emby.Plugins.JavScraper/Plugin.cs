using Emby.Plugins.JavScraper.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using Emby.Plugins.JavScraper.Data;
using Emby.Plugins.JavScraper.Scrapers;
using Emby.Plugins.JavScraper.Services;
using System.Linq;
using System.Collections.ObjectModel;
using MediaBrowser.Common;
using MediaBrowser.Controller;

namespace Emby.Plugins.JavScraper
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasThumbImage
    {
        /// <summary>
        /// 名称
        /// </summary>
        public const string NAME = "JavScraper";

        private ILogger logger;
        private ILogManager logManager;

        /// <summary>
        /// 数据库
        /// </summary>
        public ApplicationDbContext db { get; }

        /// <summary>
        /// 全部的刮削器
        /// </summary>
        public ReadOnlyCollection<AbstractScraper> Scrapers { get; }

        /// <summary>
        /// 图片服务
        /// </summary>
        public ImageProxyService ImageProxyService { get; }

        /// <summary>
        /// 翻译服务
        /// </summary>
        public TranslationService TranslationService { get; }

        /// <summary>
        /// 版本
        /// </summary>
        public static Version StaticVersion => typeof(Plugin).Assembly.GetName().Version;

        /// <summary>
        /// Gets the name of the plugin
        /// </summary>
        /// <value>The name.</value>
        public override string Name => "Jav Scraper - 详细日志版 v2025.715.30";

        /// <summary>
        /// 描述
        /// </summary>
        public override string Description => "Jav Scraper - BUG修复版 (修复内存泄漏、线程安全、资源释放、网络超时等关键BUG)";

        public static Plugin Instance { get; private set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="applicationPaths"></param>
        /// <param name="xmlSerializer"></param>
        /// <param name="logger"></param>
        public Plugin(IApplicationPaths applicationPaths, IApplicationHost applicationHost, IXmlSerializer xmlSerializer,
            IServerApplicationHost serverApplicationHost, ILogManager logManager, IJsonSerializer jsonSerializer) : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            this.logManager = logManager;
            logger = logManager.CreateLogger<Plugin>();
            logger?.Info($"{Name} - Loaded.");
            
            try
            {
            db = ApplicationDbContext.Create(applicationPaths);
                if (db == null)
                {
                    logger?.Error("Failed to create database context - plugin functionality may be limited");
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"Error creating database context: {ex.Message}");
                db = null; // 确保db为null，以便后续代码可以检查
            }
            
            // 手动创建刮削器实例
            try
            {
            var scraperList = new List<AbstractScraper>
            {
                new Scrapers.JavBus(logManager),
                new Scrapers.JavDB(logManager)
            };
            Scrapers = scraperList.AsReadOnly();
                logger?.Info($"Initialized {Scrapers.Count} scrapers: {string.Join(", ", Scrapers.Select(s => s.Name))}");
            }
            catch (Exception ex)
            {
                logger?.Error($"Error initializing scrapers: {ex.Message}");
                Scrapers = new List<AbstractScraper>().AsReadOnly();
            }

            // 创建服务实例
            try
            {
            ImageProxyService = new ImageProxyService(serverApplicationHost, jsonSerializer, logger,
                applicationHost.Resolve<MediaBrowser.Model.IO.IFileSystem>(), applicationPaths);
            TranslationService = new TranslationService(jsonSerializer, logger);
                logger?.Info("Services initialized successfully");
            }
            catch (Exception ex)
            {
                logger?.Error($"Error initializing services: {ex.Message}");
                throw; // 服务创建失败是致命错误，应该抛出
            }
        }

        public override Guid Id => new Guid("0F34B81A-4AF7-4719-9958-4CB8F680E7C6");

        public IEnumerable<PluginPageInfo> GetPages()
        {
            var type = GetType();
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = $"{type.Namespace}.Configuration.ConfigPage.html",
                    EnableInMainMenu = true,
                    MenuSection = "server",
                    MenuIcon = "theaters",
                    DisplayName = "Jav Scraper - 详细日志版 v2025.715.30",
                }
            };
        }

        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream($"{type.Namespace}.thumb.png");
        }

        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public override void SaveConfiguration()
        {
            // 验证配置
            var validationErrors = Configuration.ValidateConfiguration();
            if (validationErrors.Count > 0)
            {
                logger?.Warn($"Configuration validation found {validationErrors.Count} issue(s):");
                foreach (var error in validationErrors)
                {
                    logger?.Warn($"  - {error}");
                }
            }
            else
            {
                logger?.Info("Configuration validation passed");
            }
            
            base.SaveConfiguration();
        }

        public ILogger GetLogger(string name)
        {
            return logManager?.GetLogger(name);
        }
    }
}