﻿using Emby.Plugins.JavScraper.Configuration;
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
        /// COPY TO /volume1/@appstore/EmbyServer/releases/4.3.1.0/plugins
        /// </summary>
        /// <param name="applicationPaths"></param>
        /// <param name="xmlSerializer"></param>
        /// <param name="logger"></param>
        public Plugin(IApplicationPaths applicationPaths, IApplicationHost applicationHost, IXmlSerializer xmlSerializer,
            IServerApplicationHost serverApplicationHost, ILogManager logManager, IJsonSerializer jsonSerializer) : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            logger = logManager.CreateLogger<Plugin>();
            logger?.Info($"{Name} - Loaded.");
            db = ApplicationDbContext.Create(applicationPaths);
            Scrapers = applicationHost.GetExports<AbstractScraper>(false).Where(o => o != null).ToList().AsReadOnly();

            // 创建服务实例
            ImageProxyService = new ImageProxyService(serverApplicationHost, jsonSerializer, logger,
                applicationHost.Resolve<MediaBrowser.Model.IO.IFileSystem>(), applicationPaths);
            TranslationService = new TranslationService(jsonSerializer, logger);
        }

        public override Guid Id => new Guid("0F34B81A-4AF7-4719-9958-4CB8F680E7C6");

        public override string Name => NAME;

        public override string Description => "Jav Scraper";

        public static Plugin Instance { get; private set; }

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
                    DisplayName = "Jav Scraper",
                },
                new PluginPageInfo
                {
                    Name = "JavOrganize",
                    EmbeddedResourcePath = $"{type.Namespace}.Configuration.JavOrganizationConfigPage.html",
                    EnableInMainMenu = true,
                    MenuSection = "server",
                    MenuIcon = "theaters",
                    DisplayName = "Jav Organize",
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
            Configuration.ConfigurationVersion = DateTime.Now.Ticks;
            base.SaveConfiguration();
        }
    }
}