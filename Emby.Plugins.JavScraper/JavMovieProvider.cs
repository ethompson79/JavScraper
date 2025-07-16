using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugins.JavScraper.Configuration;
using Emby.Plugins.JavScraper.Scrapers;
using Emby.Plugins.JavScraper.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugins.JavScraper
{
    public class JavMovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>, IHasOrder
    {
        private readonly ILogger _logger;
        private readonly TranslationService translationService;
        private readonly ImageProxyService imageProxyService;
        private readonly IJsonSerializer _jsonSerializer;

        public Gfriends Gfriends { get; }

        public JavMovieProvider(ILogManager logManager, IProviderManager providerManager,
            IJsonSerializer jsonSerializer, IApplicationPaths appPaths)
        {
            _logger = logManager.CreateLogger<JavMovieProvider>();
            _jsonSerializer = jsonSerializer;

            // 从Plugin实例获取服务
            translationService = Plugin.Instance.TranslationService;
            imageProxyService = Plugin.Instance.ImageProxyService;
            Gfriends = new Gfriends(logManager, jsonSerializer);
        }

        public int Order => 4;

        public string Name => Plugin.NAME;

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
            => imageProxyService.GetImageResponse(url, ImageType.Backdrop, cancellationToken);

        public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            var metadataResult = new MetadataResult<Movie>();
            JavVideoIndex index = null;

            _logger?.Info($"{nameof(GetMetadata)} info:{_jsonSerializer.SerializeToString(info)}");

            // 获取所有搜索结果
            var searchResults = await GetSearchResults(info, cancellationToken).ConfigureAwait(false);
            if (!searchResults.Any())
            {
                _logger?.Info($"{nameof(GetMetadata)} name:{info.Name} not found 0.");
                return metadataResult;
            }

            // 从JavVideoIndex获取数据，如果没有就从搜索结果获取
            if ((index = info.GetJavVideoIndex(_jsonSerializer)) == null)
            {
                index = searchResults.FirstOrDefault()?.GetJavVideoIndex(_jsonSerializer);
                if (index == null)
                {
                    _logger?.Info($"{nameof(GetMetadata)} name:{info.Name} not found 1.");
                    return metadataResult;
                }
            }

            // 获取启用的刮削器配置，确保JavBus优先
            var enabledScrapers = Plugin.Instance?.Configuration?.GetEnableScrapers() ?? new List<Configuration.JavScraperConfigItem>();
            
            // 明确设置优先级：JavBus第一，其他按配置顺序
            var orderedScrapers = new List<Configuration.JavScraperConfigItem>();
            
            // 首先添加JavBus（如果启用）
            var javBus = enabledScrapers.FirstOrDefault(s => s.Name == "JavBus");
            if (javBus != null)
            {
                orderedScrapers.Add(javBus);
            }
            
            // 然后添加其他刮削器，保持原有顺序
            var otherScrapers = enabledScrapers.Where(s => s.Name != "JavBus").ToList();
            orderedScrapers.AddRange(otherScrapers);
            
            _logger?.Info($"{nameof(GetMetadata)} Scrapers in priority order: {string.Join(", ", orderedScrapers.Select(s => s.Name))} (JavBus priority enforced)");

            JavVideo primaryData = null;
            string primaryProvider = null;

            // 首先尝试按优先级获取主要数据
            foreach (var scraperConfig in orderedScrapers)
            {
                var scraper = Plugin.Instance.Scrapers.FirstOrDefault(s => s.Name == scraperConfig.Name);
                if (scraper == null) continue;

                try
                {
                    _logger?.Info($"{nameof(GetMetadata)} Trying primary scraper: {scraper.Name}");
                    
                    // 尝试从当前刮削器获取数据
                    JavVideoIndex scraperIndex;
                    if (index.Provider == scraper.Name)
                    {
                        scraperIndex = index;
                    }
                    else
                    {
                        // 从搜索结果中找到该刮削器的索引
                        var scraperResult = searchResults.FirstOrDefault(r => r.GetJavVideoIndex(_jsonSerializer)?.Provider == scraper.Name);
                        scraperIndex = scraperResult?.GetJavVideoIndex(_jsonSerializer);
                    }

                    if (scraperIndex != null)
                    {
                        var data = await scraper.Get(scraperIndex);
                        if (data != null)
                        {
                            Plugin.Instance.db.SaveJavVideo(data);
                            primaryData = data;
                            primaryProvider = scraper.Name;
                            _logger?.Info($"{nameof(GetMetadata)} Got primary data from: {scraper.Name}");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Error($"{nameof(GetMetadata)} Error getting data from {scraper.Name}: {ex.Message}");
                }
            }

            // 如果没有主要数据，尝试从数据库获取
            if (primaryData == null)
            {
                var scraper = Plugin.Instance.Scrapers.FirstOrDefault(o => o.Name == index.Provider);
                if (scraper != null)
                {
                    primaryData = Plugin.Instance.db.FindJavVideo(index.Provider, index.Url);
                    primaryProvider = scraper.Name;
                }
            }

            if (primaryData == null)
            {
                _logger?.Info($"{nameof(GetMetadata)} name:{info.Name} not found 2.");
                return metadataResult;
            }

            _logger?.Info($"{nameof(GetMetadata)} Primary data from {primaryProvider}: {_jsonSerializer.SerializeToString(primaryData)}");

            // 数据互补：从其他刮削器补充缺失的信息
            if (orderedScrapers.Count > 1)
            {
                await SupplementData(primaryData, orderedScrapers, searchResults, primaryProvider);
            }

            // 继续处理数据...
            var m = primaryData;

            _logger?.Info($"{nameof(GetMetadata)} name:{info.Name} {_jsonSerializer.SerializeToString(m)}");

            metadataResult.HasMetadata = true;
            metadataResult.QueriedById = true;

            //忽略部分类别
            if (m.Genres?.Any() == true)
            {
                m.Genres.RemoveAll(o => Plugin.Instance?.Configuration?.IsIgnoreGenre(o) == true);
                if (Plugin.Instance?.Configuration?.GenreIgnoreActor == true && m.Actors?.Any() == true)
                    m.Genres.RemoveAll(o => m.Actors.Contains(o));
            }

            //从标题结尾处移除女优的名字
            if (Plugin.Instance?.Configuration?.GenreIgnoreActor == true && m.Actors?.Any() == true && string.IsNullOrWhiteSpace(m.Title) == false)
            {
                var title = m.Title?.Trim();
                bool found = false;
                do
                {
                    found = false;
                    foreach (var actor in m.Actors)
                    {
                        if (title.EndsWith(actor))
                        {
                            var newTitle = title.Substring(0, title.Length - actor.Length).TrimEnd().TrimEnd(",， ".ToArray()).TrimEnd();
                            // 防止标题被完全清空
                            if (!string.IsNullOrWhiteSpace(newTitle))
                            {
                                title = newTitle;
                            found = true;
                            }
                        }
                    }
                } while (found);
                // 确保标题不为空
                if (!string.IsNullOrWhiteSpace(title))
                {
                m.Title = title;
                }
            }
            m.OriginalTitle = m.Title;

            //替换标签
            var genreReplaceMaps = Plugin.Instance.Configuration.EnableGenreReplace ? Plugin.Instance.Configuration.GetGenreReplaceMaps() : null;
            if (genreReplaceMaps?.Any() == true && m.Genres?.Any() == true)
            {
                var q =
                    from c in m.Genres
                    join p in genreReplaceMaps on c equals p.source into ps
                    from p in ps.DefaultIfEmpty()
                    select p.target ?? c;
                m.Genres = q.Where(o => !o.Contains("XXX")).ToList();
            }

            //替换演员姓名
            var actorReplaceMaps = Plugin.Instance.Configuration.EnableActorReplace ? Plugin.Instance.Configuration.GetActorReplaceMaps() : null;
            if (actorReplaceMaps?.Any() == true && m.Actors?.Any() == true)
            {
                var q =
                    from c in m.Actors
                    join p in actorReplaceMaps on c equals p.source into ps
                    from p in ps.DefaultIfEmpty()
                    select p.target ?? c;
                m.Actors = q.Where(o => !o.Contains("XXX")).ToList();
            }

            //翻译
            if (Plugin.Instance.Configuration.EnableBaiduFanyi)
            {
                var arr = new List<string>();
                var op = (BaiduFanyiOptionsEnum)Plugin.Instance.Configuration.BaiduFanyiOptions;
                if (genreReplaceMaps?.Any() == true && op.HasFlag(BaiduFanyiOptionsEnum.Genre))
                    op &= ~BaiduFanyiOptionsEnum.Genre;
                var lang = Plugin.Instance.Configuration.BaiduFanyiLanguage?.Trim();
                if (string.IsNullOrWhiteSpace(lang))
                    lang = "zh";

                if (op.HasFlag(BaiduFanyiOptionsEnum.Name) && !string.IsNullOrWhiteSpace(m.Title))
                {
                    var translatedTitle = await translationService.Fanyi(m.Title);
                    if (!string.IsNullOrWhiteSpace(translatedTitle))
                    {
                        _logger?.Info($"Translation: '{m.Title}' -> '{translatedTitle}'");
                        m.Title = translatedTitle;
                    }
                    else
                    {
                        _logger?.Warn($"Translation failed for title: '{m.Title}', keeping original");
                    }
                }

                if (op.HasFlag(BaiduFanyiOptionsEnum.Plot) && !string.IsNullOrWhiteSpace(m.Plot))
                {
                    var translatedPlot = await translationService.Fanyi(m.Plot);
                    if (!string.IsNullOrWhiteSpace(translatedPlot))
                        m.Plot = translatedPlot;
                }

                if (op.HasFlag(BaiduFanyiOptionsEnum.Genre) && m.Genres?.Any() == true)
                {
                    var translatedGenres = await translationService.Fanyi(m.Genres);
                    if (translatedGenres?.Any() == true)
                        m.Genres = translatedGenres;
                }
            }

            var cc = new[] { "-C", "-C2", "_C", "_C2" };
            if (Plugin.Instance?.Configuration?.AddChineseSubtitleGenre == true &&
                cc.Any(v => info.Name.EndsWith(v, StringComparison.OrdinalIgnoreCase)))
            {
                const string CHINESE_SUBTITLE_GENRE = "中文字幕";
                if (m.Genres == null)
                    m.Genres = new List<string>() { CHINESE_SUBTITLE_GENRE };
                else if (m.Genres.Contains(CHINESE_SUBTITLE_GENRE) == false)
                    m.Genres.Add("中文字幕");
            }

            //格式化标题
            string name = $"{m.Num} {m.Title}";
            if (string.IsNullOrWhiteSpace(Plugin.Instance?.Configuration?.TitleFormat) == false)
                name = m.GetFormatName(Plugin.Instance.Configuration.TitleFormat, Plugin.Instance.Configuration.TitleFormatEmptyValue);

            metadataResult.Item = new Movie
            {
                OfficialRating = "XXX",
                Name = name,
                Overview = m.Plot,
                ProductionYear = m.GetYear(),
                OriginalTitle = m.OriginalTitle,
                Genres = m.Genres?.ToArray() ?? new string[] { },
                SortName = m.Num,

                ExternalId = m.Num
            };

            if (m.CommunityRating >= 0 && m.CommunityRating <= 10)
                metadataResult.Item.CommunityRating = m.CommunityRating;

            if (!string.IsNullOrWhiteSpace(m.Set))
                metadataResult.Item.AddCollection(m.Set);
            if (m.Genres?.Any() == true)
                foreach (var genre in m.Genres.Where(o => !string.IsNullOrWhiteSpace(o)).Distinct())
                    metadataResult.Item.AddGenre(genre);

            metadataResult.Item.SetJavVideoIndex(_jsonSerializer, m);

            var dt = m.GetDate();
            if (dt != null)
                metadataResult.Item.PremiereDate = metadataResult.Item.DateCreated = dt.Value;

            if (!string.IsNullOrWhiteSpace(m.Studio))
                metadataResult.Item.AddStudio(m.Studio);

            var cut_persion_image = Plugin.Instance?.Configuration?.EnableCutPersonImage ?? true;
            var person_image_type = cut_persion_image ? ImageType.Primary : ImageType.Backdrop;

            //添加人员
            async Task AddPerson(string personName, PersonType personType)
            {
                var person = new PersonInfo
                {
                    Name = personName,
                    Type = personType,
                };
                var url = await Gfriends.FindAsync(person.Name, cancellationToken);
                if (url.IsWebUrl())
                {
                    person.ImageUrl = await imageProxyService.GetLocalUrl(url, person_image_type);
                }
                metadataResult.AddPerson(person);
            }

            if (!string.IsNullOrWhiteSpace(m.Director))
                await AddPerson(m.Director, PersonType.Director);

            if (m.Actors?.Any() == true)
                foreach (var actor in m.Actors)
                    await AddPerson(actor, PersonType.Actor);

            return metadataResult;
        }

        /// <summary>
        /// 番号最低满足条件：字母、数字、横杠、下划线
        /// </summary>
        private static Regex regexNum = new Regex("^[-_ a-z0-9]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
        {
            var list = new List<RemoteSearchResult>();
            if (string.IsNullOrWhiteSpace(searchInfo.Name))
                return list;

            var javid = JavIdRecognizer.Parse(searchInfo.Name);

            _logger?.Info($"{nameof(GetSearchResults)} id:{javid?.id} info:{_jsonSerializer.SerializeToString(searchInfo)}");

            //自动搜索的时候，Name=文件夹名称，有时候是不对的，需要跳过
            if (javid == null && (searchInfo.Name.Length > 12 || !regexNum.IsMatch(searchInfo.Name)))
                return list;
            var key = javid?.id ?? searchInfo.Name;
            var allScrapers = Plugin.Instance.Scrapers.ToList();
            var enabledScraperConfigs = Plugin.Instance?.Configuration?.GetEnableScrapers() ?? new List<Configuration.JavScraperConfigItem>();

            _logger?.Info($"{nameof(GetSearchResults)} Available scrapers: {string.Join(", ", allScrapers.Select(s => s.Name))}");
            _logger?.Info($"{nameof(GetSearchResults)} Enabled scrapers: {string.Join(", ", enabledScraperConfigs.Select(s => s.Name))}");

            // 按照配置顺序排序刮削器，确保JavBus优先
            var scrapers = new List<AbstractScraper>();

            // 首先添加JavBus（如果启用）
            var javBusConfig = enabledScraperConfigs.FirstOrDefault(s => s.Name == "JavBus");
            if (javBusConfig != null)
            {
                var javBusScraper = allScrapers.FirstOrDefault(s => s.Name == "JavBus");
                if (javBusScraper != null)
                {
                    scrapers.Add(javBusScraper);
                }
            }

            // 然后添加其他启用的刮削器
            foreach (var scraperConfig in enabledScraperConfigs.Where(s => s.Name != "JavBus"))
            {
                var scraper = allScrapers.FirstOrDefault(s => s.Name == scraperConfig.Name);
                if (scraper != null)
                {
                    scrapers.Add(scraper);
                }
            }

            _logger?.Info($"{nameof(GetSearchResults)} Using scrapers in priority order: {string.Join(", ", scrapers.Select(s => s.Name))} (JavBus first)");
            // 详细的代理配置调试信息
            var config = Plugin.Instance.Configuration;
            var proxyInfo = config.EnableJsProxy ? "enabled" : "disabled";
            _logger?.Info($"{nameof(GetSearchResults)} JsProxy Config: {proxyInfo}");
            _logger?.Info($"{nameof(GetSearchResults)} ProxyType: {config.ProxyType}");
            _logger?.Info($"{nameof(GetSearchResults)} JsProxy URL: {(string.IsNullOrEmpty(config.JsProxy) ? "empty" : "configured")}");
            
            var all = new List<JavVideoIndex>();

            // 按优先级顺序执行刮削器，JavBus优先
            foreach (var scraper in scrapers)
            {
                try
                {
                    _logger?.Info($"{nameof(GetSearchResults)} 正在从 {scraper.Name} 刮削: {key}");
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    {
                        var results = await scraper.Query(key).ConfigureAwait(false);
                        if (results?.Any() == true)
                        {
                            all.AddRange(results);
                            _logger?.Info($"{nameof(GetSearchResults)} {scraper.Name} 返回 {results.Count} 个结果");
                        }
                        else
                        {
                            _logger?.Info($"{nameof(GetSearchResults)} {scraper.Name} 未找到结果");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger?.Warn($"{nameof(GetSearchResults)} {scraper.Name} 刮削超时 (15秒): {key}");
                }
                catch (Exception ex)
                {
                    _logger?.Error($"{nameof(GetSearchResults)} {scraper.Name} 刮削出错: {ex.Message}");
                }
            }

            _logger?.Info($"{nameof(GetSearchResults)} name:{searchInfo.Name} id:{javid?.id} count:{all.Count}");

            if (all.Any() != true)
            {
                _logger?.Warn($"{nameof(GetSearchResults)} 所有刮削器都未找到结果: {key}");
                return list;
            }

            all = scrapers
                  .Join(all.GroupBy(o => o.Provider),
                  o => o.Name,
                  o => o.Key, (o, v) => v)
                  .SelectMany(o => o)
                  .ToList();

            foreach (var m in all)
            {
                var result = new RemoteSearchResult
                {
                    Name = $"{m.Num} {m.Title}",
                    ProductionYear = m.GetYear(),
                    ImageUrl = await imageProxyService.GetLocalUrl(m.Cover, with_api_url: false),
                    SearchProviderName = Name,
                    PremiereDate = m.GetDate(),
                };
                result.SetJavVideoIndex(_jsonSerializer, m);
                list.Add(result);
            }
            return list;
        }

        /// <summary>
        /// 数据互补：从其他刮削器补充缺失的信息
        /// </summary>
        private async Task SupplementData(JavVideo primaryData, List<Configuration.JavScraperConfigItem> orderedScrapers, 
            IEnumerable<RemoteSearchResult> searchResults, string primaryProvider)
        {
            _logger?.Info($"SupplementData: Starting data supplementation for primary provider: {primaryProvider}");

            foreach (var scraperConfig in orderedScrapers)
            {
                if (scraperConfig.Name == primaryProvider) continue; // 跳过主提供者

                var scraper = Plugin.Instance.Scrapers.FirstOrDefault(s => s.Name == scraperConfig.Name);
                if (scraper == null) continue;

                try
                {
                    _logger?.Info($"SupplementData: Trying to supplement from: {scraper.Name}");

                    // 从搜索结果中找到该刮削器的索引
                    var scraperResult = searchResults.FirstOrDefault(r => r.GetJavVideoIndex(_jsonSerializer)?.Provider == scraper.Name);
                    var scraperIndex = scraperResult?.GetJavVideoIndex(_jsonSerializer);

                    if (scraperIndex != null)
                    {
                        var supplementData = await scraper.Get(scraperIndex);
                        if (supplementData != null)
                        {
                            Plugin.Instance.db.SaveJavVideo(supplementData);
                            
                            // 补充缺失的数据
                            bool hasChanges = false;

                            // 补充标题（如果主数据的标题为空或只是番号）
                            if (string.IsNullOrWhiteSpace(primaryData.Title) || 
                                (!string.IsNullOrWhiteSpace(primaryData.Num) && primaryData.Title.Equals(primaryData.Num, StringComparison.OrdinalIgnoreCase)))
                            {
                                if (!string.IsNullOrWhiteSpace(supplementData.Title) && 
                                    !supplementData.Title.Equals(primaryData.Num, StringComparison.OrdinalIgnoreCase))
                                {
                                    _logger?.Info($"SupplementData: Supplementing title from {scraper.Name}: '{supplementData.Title}'");
                                    primaryData.Title = supplementData.Title;
                                    hasChanges = true;
                                }
                            }

                            // 补充简介
                            if (string.IsNullOrWhiteSpace(primaryData.Plot) && !string.IsNullOrWhiteSpace(supplementData.Plot))
                            {
                                _logger?.Info($"SupplementData: Supplementing plot from {scraper.Name}");
                                primaryData.Plot = supplementData.Plot;
                                hasChanges = true;
                            }

                            // 补充封面
                            if (string.IsNullOrWhiteSpace(primaryData.Cover) && !string.IsNullOrWhiteSpace(supplementData.Cover))
                            {
                                _logger?.Info($"SupplementData: Supplementing cover from {scraper.Name}");
                                primaryData.Cover = supplementData.Cover;
                                hasChanges = true;
                            }

                            // 补充导演
                            if (string.IsNullOrWhiteSpace(primaryData.Director) && !string.IsNullOrWhiteSpace(supplementData.Director))
                            {
                                _logger?.Info($"SupplementData: Supplementing director from {scraper.Name}");
                                primaryData.Director = supplementData.Director;
                                hasChanges = true;
                            }

                            // 补充制作商信息
                            if (string.IsNullOrWhiteSpace(primaryData.Studio) && !string.IsNullOrWhiteSpace(supplementData.Studio))
                            {
                                _logger?.Info($"SupplementData: Supplementing studio from {scraper.Name}");
                                primaryData.Studio = supplementData.Studio;
                                hasChanges = true;
                            }

                            if (string.IsNullOrWhiteSpace(primaryData.Maker) && !string.IsNullOrWhiteSpace(supplementData.Maker))
                            {
                                _logger?.Info($"SupplementData: Supplementing maker from {scraper.Name}");
                                primaryData.Maker = supplementData.Maker;
                                hasChanges = true;
                            }

                            // 补充系列
                            if (string.IsNullOrWhiteSpace(primaryData.Set) && !string.IsNullOrWhiteSpace(supplementData.Set))
                            {
                                _logger?.Info($"SupplementData: Supplementing set from {scraper.Name}");
                                primaryData.Set = supplementData.Set;
                                hasChanges = true;
                            }

                            // 补充时长
                            if (string.IsNullOrWhiteSpace(primaryData.Runtime) && !string.IsNullOrWhiteSpace(supplementData.Runtime))
                            {
                                _logger?.Info($"SupplementData: Supplementing runtime from {scraper.Name}");
                                primaryData.Runtime = supplementData.Runtime;
                                hasChanges = true;
                            }

                            // 补充评分
                            if (!primaryData.CommunityRating.HasValue && supplementData.CommunityRating.HasValue)
                            {
                                _logger?.Info($"SupplementData: Supplementing rating from {scraper.Name}: {supplementData.CommunityRating}");
                                primaryData.CommunityRating = supplementData.CommunityRating;
                                hasChanges = true;
                            }

                            // 补充类别（合并唯一值）
                            if (supplementData.Genres?.Any() == true)
                            {
                                if (primaryData.Genres == null) primaryData.Genres = new List<string>();
                                var newGenres = supplementData.Genres.Where(g => !primaryData.Genres.Contains(g, StringComparer.OrdinalIgnoreCase)).ToList();
                                if (newGenres.Any())
                                {
                                    _logger?.Info($"SupplementData: Adding {newGenres.Count} genres from {scraper.Name}: {string.Join(", ", newGenres)}");
                                    primaryData.Genres.AddRange(newGenres);
                                    hasChanges = true;
                                }
                            }

                            // 补充演员（合并唯一值）
                            if (supplementData.Actors?.Any() == true)
                            {
                                if (primaryData.Actors == null) primaryData.Actors = new List<string>();
                                var newActors = supplementData.Actors.Where(a => !primaryData.Actors.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();
                                if (newActors.Any())
                                {
                                    _logger?.Info($"SupplementData: Adding {newActors.Count} actors from {scraper.Name}: {string.Join(", ", newActors)}");
                                    primaryData.Actors.AddRange(newActors);
                                    hasChanges = true;
                                }
                            }

                            // 补充样片图片（合并唯一值）
                            if (supplementData.Samples?.Any() == true)
                            {
                                if (primaryData.Samples == null) primaryData.Samples = new List<string>();
                                var newSamples = supplementData.Samples.Where(s => !primaryData.Samples.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
                                if (newSamples.Any())
                                {
                                    _logger?.Info($"SupplementData: Adding {newSamples.Count} samples from {scraper.Name}");
                                    primaryData.Samples.AddRange(newSamples);
                                    hasChanges = true;
                                }
                            }

                            if (hasChanges)
                            {
                                _logger?.Info($"SupplementData: Successfully supplemented data from {scraper.Name}");
                                // 保存更新的数据
                                Plugin.Instance.db.SaveJavVideo(primaryData);
                            }
                            else
                            {
                                _logger?.Info($"SupplementData: No supplemental data needed from {scraper.Name}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Error($"SupplementData: Error supplementing from {scraper.Name}: {ex.Message}");
                }
            }

            _logger?.Info($"SupplementData: Data supplementation completed");
        }
    }
}