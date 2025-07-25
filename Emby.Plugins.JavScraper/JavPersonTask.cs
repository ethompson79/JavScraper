﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugins.JavScraper.Scrapers;
using Emby.Plugins.JavScraper.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Logging;

namespace Emby.Plugins.JavScraper
{
    public class JavPersonTask : IScheduledTask
    {
        private readonly ILibraryManager libraryManager;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ImageProxyService imageProxyService;
        private readonly IFileSystem fileSystem;
        private readonly ILogger _logger;

        public Gfriends Gfriends { get; }

        public JavPersonTask(ILogManager logManager, ILibraryManager libraryManager,
            IJsonSerializer _jsonSerializer, IApplicationPaths appPaths,
            IProviderManager providerManager, IFileSystem fileSystem)
        {
            _logger = logManager.CreateLogger<JavPersonTask>();
            this.libraryManager = libraryManager;
            this._jsonSerializer = _jsonSerializer;
            this.fileSystem = fileSystem;

            // 从Plugin实例获取服务
            imageProxyService = Plugin.Instance.ImageProxyService;
            Gfriends = new Gfriends(logManager, _jsonSerializer);
        }

        public string Name => Plugin.NAME + ": 采集缺失的女优头像和信息";
        public string Key => Plugin.NAME + "-Actress";
        public string Description => "采集缺失的女优头像和信息";
        public string Category => "JavScraper";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            var t = new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerWeekly,
                TimeOfDayTicks = TimeSpan.FromHours(2).Ticks,
                MaxRuntimeTicks = TimeSpan.FromHours(3).Ticks,
                DayOfWeek = DayOfWeek.Monday
            };
            return new[] { t };
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info($"Running...");
            progress.Report(0);

            IDirectoryService ds = default;

            var dstype = typeof(DirectoryService);
            var cr = dstype.GetConstructors().Where(o => o.IsPublic && o.IsStatic == false).OrderByDescending(o => o.GetParameters().Length).FirstOrDefault();
            if (cr.GetParameters().Length == 1)
                ds = cr.Invoke(new[] { fileSystem }) as IDirectoryService;
            else
                ds = cr.Invoke(new object[] { _logger, fileSystem }) as IDirectoryService;

            var query = new InternalItemsQuery()
            {
                IncludeItemTypes = new[] { nameof(Person) },
                PersonTypes = new[] { PersonType.Actor }
            };

            var persons = libraryManager.GetItemList(query)?.ToList();

            if (persons?.Any() != true)
            {
                progress.Report(100);
                return;
            }
            persons.RemoveAll(o => !(o is Person));

            for (int i = 0; i < persons.Count; ++i)
            {
                var person = persons[i];

                MetadataRefreshMode imageRefreshMode = 0;
                MetadataRefreshMode metadataRefreshMode = 0;

                if (!person.HasImage(ImageType.Primary))
                    imageRefreshMode = MetadataRefreshMode.Default;
                if (string.IsNullOrEmpty(person.Overview))
                    metadataRefreshMode = MetadataRefreshMode.FullRefresh;

                if (imageRefreshMode == 0 && metadataRefreshMode == 0)
                    continue;

                var options = new MetadataRefreshOptions(ds)
                {
                    ImageRefreshMode = imageRefreshMode,
                    MetadataRefreshMode = metadataRefreshMode
                };

                try
                {
                    await person.RefreshMetadata(options, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Failed to refresh metadata for person {person.Name}: {ex.Message}");
                }
                progress.Report(i * 1.0 / persons.Count * 100);
            }

            progress.Report(100);
        }
    }
}