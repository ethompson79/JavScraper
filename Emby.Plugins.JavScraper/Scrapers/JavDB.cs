using HtmlAgilityPack;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Net.Http;
using Emby.Plugins.JavScraper.Http;

namespace Emby.Plugins.JavScraper.Scrapers
{
    /// <summary>
    /// https://www.javbus.com/BIJN-172
    /// </summary>
    public class JavDB : AbstractScraper
    {
        /// <summary>
        /// 适配器名称
        /// </summary>
        public override string Name => "JavDB";

        /// <summary>
        /// 番号分段识别
        /// </summary>
        private static Regex regex = new Regex("((?<a>[a-z]{2,})|(?<b>[0-9]{2,}))", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// 构造
        /// </summary>
        /// <param name="handler"></param>
        public JavDB(ILogManager logManager)
            : base("https://javdb.com/", logManager.CreateLogger<JavDB>())
        {
        }
        
        /// <summary>
        /// 重写BaseUrl设置，添加JavDB特殊配置 - 速度优化版
        /// </summary>
        public override string BaseUrl
        {
            get => base.BaseUrl;
            set
            {
                if (value.IsWebUrl() != true)
                    return;
                if (base.BaseUrl == value && client != null)
                    return;
                    
                // 直接设置基类的私有字段，避免无限递归
                var baseUrlField = typeof(AbstractScraper).GetField("base_url", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                baseUrlField?.SetValue(this, value);
                    
                // 创建专门为JavDB配置的HttpClient - 速度优化版
                client = new HttpClientEx(client =>
                {
                    client.BaseAddress = new Uri(value);
                    client.Timeout = TimeSpan.FromSeconds(15); // 快速超时 - 15秒无响应说明网络连接有问题
                    
                    // 应用基础优化配置
                    ConfigureHttpClientOptimized(client);
                    
                    // JavDB特殊配置
                    ConfigureJavDBSpecialHeaders(client);
                    
                    // 配置JavDB的HttpClientHandler优化
                    ConfigureJavDBHandlerOptimizations(client);
                });
                
                log?.Info($"JavDB BaseUrl configured with anti-bot optimizations: {value}");
            }
        }
        
        /// <summary>
        /// 配置JavDB特殊请求头
        /// </summary>
        private void ConfigureJavDBSpecialHeaders(HttpClient client)
        {
            // JavDB特定的请求头配置
            client.DefaultRequestHeaders.Remove("Accept");
            client.DefaultRequestHeaders.Add("Accept", 
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            
            // JavDB偏好的Sec-Fetch配置
            client.DefaultRequestHeaders.Remove("Sec-Fetch-Site");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
            
            client.DefaultRequestHeaders.Remove("Sec-Fetch-Mode");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            
            // 添加JavDB特有的请求头
            client.DefaultRequestHeaders.Remove("X-Requested-With");
            if (random.Value.Next(2) == 0) // 50%概率添加
            {
                client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            }
            
            log?.Debug("JavDB特殊请求头配置完成");
        }
        
        /// <summary>
        /// 配置JavDB的HttpClientHandler优化
        /// </summary>
        private void ConfigureJavDBHandlerOptimizations(HttpClient client)
        {
            try
            {
                // 获取HttpClientHandler并设置优化配置
                var handler = client.GetType().GetField("_handler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(client) as HttpClientHandler;
                if (handler != null)
                {
                    handler.MaxConnectionsPerServer = 12; // 提升并发连接数
                    handler.UseProxy = false; // 禁用代理检测，提升速度
                    handler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate; // 支持压缩
                    handler.UseCookies = true; // 启用Cookie管理
                    
                    // 设置Cookie容器以支持会话管理
                    if (handler.CookieContainer == null)
                    {
                        handler.CookieContainer = new System.Net.CookieContainer();
                    }
                    
                    log?.Debug($"JavDB HttpClientHandler优化配置完成");
                }
            }
            catch (Exception ex)
            {
                log?.Warn($"Failed to configure JavDB handler optimizations: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查关键字是否符合
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public override bool CheckKey(string key)
            => JavIdRecognizer.FC2(key) == null;

        /// <summary>
        /// 获取列表
        /// </summary>
        /// <param name="key">关键字</param>
        /// <returns></returns>
        protected override async Task<List<JavVideoIndex>> DoQyery(List<JavVideoIndex> ls, string key)
        {
            ///https://javdb.com/search?q=ADN-106&f=all
            var doc = await GetHtmlDocumentAsync($"/search?q={key}&f=all");
            if (doc != null)
                ParseIndex(ls, doc);

            if (ls.Any())
            {
                var ks = regex.Matches(key).Cast<Match>()
                     .Select(o => o.Groups[0].Value).ToList();

                ls.RemoveAll(i =>
                {
                    foreach (var k in ks)
                    {
                        if (i.Num?.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) //包含，则继续
                            continue;
                        if (k[0] != '0') //第一个不是0，则不用继续了。
                            return true;//移除

                        var k2 = k.TrimStart('0');
                        if (i.Num?.IndexOf(k2, StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        return true; //移除
                    }
                    return false; //保留
                });
            }

            SortIndex(key, ls);
            return ls;
        }

        /// <summary>
        /// 解析列表
        /// </summary>
        /// <param name="ls"></param>
        /// <param name="doc"></param>
        /// <returns></returns>
        protected override List<JavVideoIndex> ParseIndex(List<JavVideoIndex> ls, HtmlDocument doc)
        {
            if (doc == null)
            {
                log?.Warn("JavDB ParseIndex: Document is null");
                return ls;
            }

            // 尝试多种选择器
            var selectors = new[]
            {
                "//*[@id='videos']/div/div/a",
                "//*[@id='videos']//a",
                "//div[@class='video-item']//a",
                "//div[contains(@class,'video-item')]//a",
                "//div[@class='grid-item']//a",
                "//div[contains(@class,'grid-item')]//a",
                "//div[@class='item']//a",
                "//a[contains(@class,'box')]"
            };

            HtmlNodeCollection nodes = null;
            foreach (var selector in selectors)
            {
                nodes = doc.DocumentNode.SelectNodes(selector);
                if (nodes?.Any() == true)
                {
                    log?.Info($"JavDB ParseIndex: Found {nodes.Count} nodes using selector: {selector}");
                    break;
                }
            }

            if (nodes?.Any() != true)
            {
                log?.Warn("JavDB ParseIndex: No video nodes found with any selector");
                return ls;
            }

            foreach (var node in nodes)
            {
                var url = node.GetAttributeValue("href", null);
                if (string.IsNullOrWhiteSpace(url))
                    continue;
                var m = new JavVideoIndex() { Provider = Name, Url = new Uri(client.BaseAddress, url).ToString() };
                ls.Add(m);

                // 尝试多种图片选择器
                var imgSelectors = new[]
                {
                    "./div/img",
                    ".//img",
                    "./div[@class='cover']//img",
                    "./div[contains(@class,'cover')]//img"
                };

                foreach (var imgSelector in imgSelectors)
                {
                    var img = node.SelectSingleNode(imgSelector);
                if (img != null)
                {
                    m.Cover = img.GetAttributeValue("data-original", null);
                    if (string.IsNullOrEmpty(m.Cover)) 
                        m.Cover = img.GetAttributeValue("data-src", null);
                    if (string.IsNullOrEmpty(m.Cover)) 
                        m.Cover = img.GetAttributeValue("src", null);
                    if (m.Cover?.StartsWith("//") == true)
                        m.Cover = $"https:{m.Cover}";
                        break;
                    }
                }

                // 先设置标题
                var titleSelectors = new[]
                {
                    "./div[@class='video-title']",
                    "./div[@class='video-title2']",
                    ".//div[@class='video-title']",
                    ".//div[@class='video-title2']",
                    ".//div[@class='title']",
                    ".//span[@class='title']",
                    ".//h3"
                };

                foreach (var titleSelector in titleSelectors)
                {
                    var titleNode = node.SelectSingleNode(titleSelector);
                    if (titleNode != null)
                    {
                        m.Title = titleNode.InnerText.Trim();
                        if (!string.IsNullOrEmpty(m.Title))
                            break;
                    }
                }

                // 然后从标题中提取番号（优先）
                var titleText = m.Title ?? "";
                var numFromTitle = System.Text.RegularExpressions.Regex.Match(titleText, @"([A-Za-z]+[-_]?\d+)");
                if (numFromTitle.Success)
                {
                    m.Num = numFromTitle.Groups[1].Value;
                    log?.Info($"JavDB ParseIndex: Extracted Num from title: {m.Num}");
                }
                else
                {
                    // 尝试传统的番号选择器
                    var numSelectors = new[]
                    {
                        "./div[@class='uid']",
                        "./div[@class='uid2']",
                        ".//div[@class='uid']",
                        ".//div[@class='uid2']",
                        ".//span[@class='uid']",
                        ".//span[@class='video-code']",
                        ".//div[@class='video-code']"
                    };

                    foreach (var numSelector in numSelectors)
                    {
                        var numNode = node.SelectSingleNode(numSelector);
                        if (numNode != null)
                        {
                            var numText = numNode.InnerText.Trim();
                            var numMatch = System.Text.RegularExpressions.Regex.Match(numText, @"([A-Za-z]+[-_]?\d+)");
                            if (numMatch.Success)
                            {
                                m.Num = numMatch.Groups[1].Value;
                                log?.Info($"JavDB ParseIndex: Extracted Num from selector: {m.Num}");
                                break;
                            }
                        }
                    }

                    // 如果所有方法都失败，尝试从URL中提取作为最后手段
                    if (string.IsNullOrWhiteSpace(m.Num) && !string.IsNullOrWhiteSpace(url))
                    {
                        // URL格式通常是 /v/xxxxx，尝试提取xxxxx作为临时ID
                        var urlMatch = System.Text.RegularExpressions.Regex.Match(url, @"/v/([a-zA-Z0-9]+)");
                        if (urlMatch.Success)
                        {
                            var tempId = urlMatch.Groups[1].Value;
                            log?.Info($"JavDB ParseIndex: Using URL ID as temp Num: {tempId} for URL: {url}");
                            m.Num = tempId; // 暂时使用URL中的ID
                        }
                    }
                }

                // 尝试多种日期选择器
                var dateSelectors = new[]
                {
                    "./div[@class='meta']",
                    ".//div[@class='meta']",
                    ".//div[@class='date']",
                    ".//span[@class='date']",
                    ".//div[@class='info']"
                };

                foreach (var dateSelector in dateSelectors)
                {
                    var dateNode = node.SelectSingleNode(dateSelector);
                    if (dateNode != null)
                    {
                        m.Date = dateNode.InnerText.Trim();
                        if (!string.IsNullOrEmpty(m.Date))
                            break;
                    }
                }

                // 清理标题中的番号
                if (string.IsNullOrWhiteSpace(m.Num) == false && m.Title?.StartsWith(m.Num, StringComparison.OrdinalIgnoreCase) == true)
                    m.Title = m.Title.Substring(m.Num.Length).Trim();

                if (string.IsNullOrWhiteSpace(m.Num))
                {
                    log?.Warn($"JavDB ParseIndex: No Num found for URL: {url}");
                    continue;
                }

                log?.Info($"JavDB ParseIndex: Found movie - Num: {m.Num}, Title: {m.Title}, URL: {url}");
            }

            log?.Info($"JavDB ParseIndex: Total movies found: {ls.Count}");
            return ls;
        }

        /// <summary>
        /// 获取完整视频信息 - 融合测试工具验证的增强逻辑
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public override async Task<JavVideo> Get(string url)
        {
            try
            {
                log?.Info($"JavDB: 开始刮削 URL: {url}");
                
                // 添加智能延迟，模拟人类行为
                await Task.Delay(random.Value.Next(200, 500));
                
                //https://javdb.com/v/BzbA6
                var doc = await GetHtmlDocumentAsync(url);
                if (doc == null)
                {
                    log?.Warn($"JavDB: 无法获取HTML文档: {url}");
                    return null;
                }

                var movie = new JavVideo() { Provider = Name, Url = url };

                // 增强的标题提取
                movie.Title = ExtractTitleEnhanced(doc);
                log?.Debug($"JavDB: 标题提取结果: {movie.Title}");

                // 增强的信息提取 - 多种方式获取元数据
                var infoDict = ExtractInfoDictionaryEnhanced(doc);
                
                // 使用增强的值获取方法
                movie.Num = GetValueEnhanced(infoDict, new[] { "番號", "番号", "ID", "识别码", "識別碼" });
                movie.Date = GetValueEnhanced(infoDict, new[] { "日期", "Date", "发行日期" });
                movie.Runtime = GetValueEnhanced(infoDict, new[] { "時長", "时长", "Runtime" });
                movie.Maker = GetValueEnhanced(infoDict, new[] { "發行", "发行", "Maker" });
                movie.Studio = GetValueEnhanced(infoDict, new[] { "片商", "Studio" });
                movie.Set = GetValueEnhanced(infoDict, new[] { "系列", "Series" });
                movie.Director = GetValueEnhanced(infoDict, new[] { "導演", "导演", "Director" });

                // 增强的演员提取
                movie.Actors = ExtractActorsEnhanced(doc, infoDict);
                log?.Debug($"JavDB: 演员提取完成: {string.Join(", ", movie.Actors ?? new List<string>())}");

                // 增强的标签提取
                movie.Genres = ExtractGenresEnhanced(doc, infoDict);
                log?.Debug($"JavDB: 标签提取完成: {string.Join(", ", movie.Genres ?? new List<string>())}");

                // 增强的封面提取
                movie.Cover = ExtractCoverEnhanced(doc);
                log?.Debug($"JavDB: 封面提取结果: {movie.Cover}");

                // 增强的样品图片提取
                movie.Samples = ExtractSamplesEnhanced(doc);
                log?.Info($"JavDB: 图片提取完成，共 {movie.Samples?.Count ?? 0} 张");

                // 增强的评分提取
                movie.CommunityRating = ExtractRatingEnhanced(infoDict);

                log?.Info($"JavDB: 刮削完成 - 标题: {movie.Title}, 演员: {movie.Actors?.Count ?? 0}人, 标签: {movie.Genres?.Count ?? 0}个, 图片: {movie.Samples?.Count ?? 0}张");

                // 清理标题中的番号 (保持原有逻辑)
                if (!string.IsNullOrWhiteSpace(movie.Num) && movie.Title?.StartsWith(movie.Num, StringComparison.OrdinalIgnoreCase) == true)
                {
                    movie.Title = movie.Title.Substring(movie.Num.Length).Trim();
                }

                return movie;
            }
            catch (Exception ex)
            {
                log?.Error($"JavDB: 刮削出错: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 增强的标题提取方法
        /// </summary>
        private string ExtractTitleEnhanced(HtmlDocument doc)
        {
            var titleSelectors = new[]
            {
                "//h2[@class='title is-4']",
                "//*[contains(@class,'title')]/strong",
                "//h1[@class='title']",
                "//h2[@class='title']",
                "//div[@class='video-detail']//h1",
                "//div[@class='video-detail']//h2",
                "//title"
            };

            foreach (var selector in titleSelectors)
            {
                var titleNode = doc.DocumentNode.SelectSingleNode(selector);
                if (titleNode != null)
                {
                    var title = CleanTitleEnhanced(titleNode.InnerText);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        log?.Debug($"JavDB: 标题提取成功，使用选择器: {selector}");
                        return title;
                    }
                }
            }
            return null;
        }
        
        /// <summary>
        /// 增强的信息字典提取
        /// </summary>
        private Dictionary<string, string> ExtractInfoDictionaryEnhanced(HtmlDocument doc)
        {
            var dic = new Dictionary<string, string>();
            
            // 尝试多种面板选择器
            var panelSelectors = new[]
            {
                "//div[contains(@class,'panel-block')]",
                "//div[contains(@class,'info')]//div",
                "//div[@class='movie-panel-info']//div",
                "//div[@class='panel']//div"
            };

            HtmlNodeCollection nodes = null;
            foreach (var selector in panelSelectors)
            {
                nodes = doc.DocumentNode.SelectNodes(selector);
                if (nodes?.Any() == true)
                {
                    log?.Debug($"JavDB: 找到 {nodes.Count} 个信息节点，使用选择器: {selector}");
                    break;
                }
            }

            if (nodes?.Any() == true)
            {
                foreach (var n in nodes)
                {
                    var k = n.SelectSingleNode("./strong")?.InnerText?.Trim();
                    string v = null;
                    
                    if (k?.Contains("演員") == true || k?.Contains("演员") == true || k?.Contains("Actress") == true)
                    {
                        var ac = n.SelectNodes("./*[@class='value']/a");
                        if (ac?.Any() == true)
                            v = string.Join(",", ac.Select(o => o.InnerText?.Trim()));
                    }

                    if (v == null)
                        v = n.SelectSingleNode("./*[@class='value']")?.InnerText?.Trim().Replace("&nbsp;", " ");

                    if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v))
                    {
                        dic[k] = v;
                        log?.Debug($"JavDB: 提取信息 - {k}: {v}");
                    }
                }
            }
            
            return dic;
        }
        
        /// <summary>
        /// 增强的值获取方法
        /// </summary>
        private string GetValueEnhanced(Dictionary<string, string> dic, string[] keys)
        {
            foreach (var key in keys)
            {
                var result = dic.Where(o => o.Key.Contains(key)).Select(o => o.Value).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }
            return null;
        }
        
        /// <summary>
        /// 增强的演员提取方法
        /// </summary>
        private List<string> ExtractActorsEnhanced(HtmlDocument doc, Dictionary<string, string> infoDict)
        {
            var keys = new[] { "演員", "演员", "Actress", "Actor" };
            foreach (var key in keys)
            {
                var v = GetValueEnhanced(infoDict, new[] { key });
                if (!string.IsNullOrWhiteSpace(v))
                {
                    var actors = v.Split(',').Select(o => o.Trim()).Distinct().ToList();
                    for (int i = 0; i < actors.Count; i++)
                    {
                        var a = actors[i];
                        if (a.Contains("("))
                        {
                            var arr = a.Split("()".ToArray(), StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
                            if (arr.Length == 2)
                                actors[i] = arr[1];
                        }
                    }
                    return actors;
                }
            }
            
            // 备用方法：直接从DOM提取
            var actorNodes = doc.DocumentNode.SelectNodes("//strong[text()='演員:']/following-sibling::span//a") ??
                            doc.DocumentNode.SelectNodes("//span[contains(@class, 'actor')]//a");
            if (actorNodes != null)
            {
                return actorNodes.Select(node => node.InnerText?.Trim()).Where(name => !string.IsNullOrEmpty(name)).ToList();
            }
            
            return new List<string>();
        }
        
        /// <summary>
        /// 增强的标签提取方法
        /// </summary>
        private List<string> ExtractGenresEnhanced(HtmlDocument doc, Dictionary<string, string> infoDict)
        {
            var keys = new[] { "類別", "类别", "Genre", "标签", "Tag" };
            foreach (var key in keys)
            {
                var v = GetValueEnhanced(infoDict, new[] { key });
                if (!string.IsNullOrWhiteSpace(v))
                {
                    return v.Split(',').Select(o => o.Trim()).Distinct().ToList();
                }
            }
            
            // 备用方法：直接从DOM提取
            var tagNodes = doc.DocumentNode.SelectNodes("//strong[text()='類別:']/following-sibling::span//a") ??
                          doc.DocumentNode.SelectNodes("//span[contains(@class, 'tag')]//a");
            if (tagNodes != null)
            {
                return tagNodes.Select(node => node.InnerText?.Trim()).Where(tag => !string.IsNullOrEmpty(tag)).ToList();
            }
            
            return new List<string>();
        }
        
        /// <summary>
        /// 增强的封面提取方法
        /// </summary>
        private string ExtractCoverEnhanced(HtmlDocument doc)
        {
            var coverSelectors = new[]
            {
                "//img[@class='video-cover']",
                "//img[contains(@class,'video-cover')]",
                "//img[contains(@class,'cover')]",
                "//div[@class='video-meta-panel']//img",
                "//div[@class='column column-video-cover']//img"
            };

            foreach (var selector in coverSelectors)
            {
                var coverNode = doc.DocumentNode.SelectSingleNode(selector);
                if (coverNode != null)
                {
                    var img = coverNode.GetAttributeValue("data-original", null);
                    if (string.IsNullOrEmpty(img))
                        img = coverNode.GetAttributeValue("data-src", null);
                    if (string.IsNullOrEmpty(img)) 
                        img = coverNode.GetAttributeValue("src", null);

                    if (!string.IsNullOrWhiteSpace(img))
                    {
                        if (!img.StartsWith("http"))
                            img = "https://javdb.com" + img;
                        log?.Debug($"JavDB: 封面提取成功，使用选择器: {selector}");
                        return img;
                    }
                }
            }

            // 尝试meta标签
            var metaImg = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", null);
            if (!string.IsNullOrWhiteSpace(metaImg))
                return metaImg;

            return null;
        }
        
        /// <summary>
        /// 增强的样品图片提取方法 - 优化图片质量
        /// </summary>
        private List<string> ExtractSamplesEnhanced(HtmlDocument doc)
        {
            var samples = new List<string>();

            // 方法1: 尝试获取高清图片链接（从a标签的href）
            var highQualitySelectors = new[]
            {
                "//div[@class='tile-images preview-images']/a",
                "//div[contains(@class,'preview-images')]//a",
                "//div[@class='sample-box']//a"
            };

            foreach (var selector in highQualitySelectors)
            {
                var linkNodes = doc.DocumentNode.SelectNodes(selector);
                if (linkNodes?.Any() == true)
                {
                    var highQualityUrls = linkNodes.Select(o => o.GetAttributeValue("href", null))
                        .Where(o => !string.IsNullOrWhiteSpace(o) && !o.StartsWith('#'))
                        .ToList();

                    if (highQualityUrls.Any())
                    {
                        log?.Debug($"JavDB: 高清样品图片提取成功，使用选择器: {selector}，共 {highQualityUrls.Count} 张");
                        samples.AddRange(highQualityUrls);
                        break; // 找到高清图片就不再尝试其他方法
                    }
                }
            }

            // 方法2: 如果没有找到高清链接，尝试直接获取img的src（备用方案）
            if (!samples.Any())
            {
                var imgSelectors = new[]
                {
                    "//div[@class='tile-images preview-images']//img",
                    "//div[contains(@class,'preview-images')]//img",
                    "//div[@class='sample-box']//img"
                };

                foreach (var selector in imgSelectors)
                {
                    var imgNodes = doc.DocumentNode.SelectNodes(selector);
                    if (imgNodes?.Any() == true)
                    {
                        var imgUrls = imgNodes.Select(o => o.GetAttributeValue("src", null))
                            .Where(o => !string.IsNullOrWhiteSpace(o))
                            .Select(url => ConvertToHighQualityUrl(url)) // 尝试转换为高清URL
                            .ToList();

                        if (imgUrls.Any())
                        {
                            log?.Debug($"JavDB: 样品图片提取成功（备用方案），使用选择器: {selector}，共 {imgUrls.Count} 张");
                            samples.AddRange(imgUrls);
                            break;
                        }
                    }
                }
            }

            log?.Info($"JavDB: 样品图片提取完成，共获取 {samples.Count} 张图片");
            return samples;
        }

        /// <summary>
        /// 尝试将缩略图URL转换为高清图URL
        /// </summary>
        private string ConvertToHighQualityUrl(string thumbnailUrl)
        {
            if (string.IsNullOrWhiteSpace(thumbnailUrl))
                return thumbnailUrl;

            try
            {
                // JavDB的图片URL模式转换
                // 缩略图: https://c0.jdbstatic.com/thumbs/wq/wqAB7_l_1.jpg
                // 高清图: https://c0.jdbstatic.com/samples/wq/wqAB7_l_1.jpg
                if (thumbnailUrl.Contains("/thumbs/"))
                {
                    var highQualityUrl = thumbnailUrl.Replace("/thumbs/", "/samples/");
                    log?.Debug($"JavDB: 图片URL转换 - 缩略图: {thumbnailUrl} -> 高清: {highQualityUrl}");
                    return highQualityUrl;
                }

                // 如果URL中包含尺寸参数，尝试移除或修改
                if (thumbnailUrl.Contains("_thumb") || thumbnailUrl.Contains("_small"))
                {
                    var highQualityUrl = thumbnailUrl.Replace("_thumb", "").Replace("_small", "");
                    log?.Debug($"JavDB: 图片URL优化 - 原始: {thumbnailUrl} -> 优化: {highQualityUrl}");
                    return highQualityUrl;
                }
            }
            catch (Exception ex)
            {
                log?.Error($"JavDB: 图片URL转换失败: {ex.Message}");
            }

            return thumbnailUrl; // 如果转换失败，返回原URL
        }
        
        /// <summary>
        /// 增强的评分提取方法
        /// </summary>
        private float? ExtractRatingEnhanced(Dictionary<string, string> infoDict)
        {
            var value = GetValueEnhanced(infoDict, new[] { "評分", "评分", "Rating" });
            if (string.IsNullOrWhiteSpace(value))
                return null;
                
            var m = Regex.Match(value, @"(?<rating>[\d.]+)分?");
            if (m.Success && float.TryParse(m.Groups["rating"].Value, out var rating))
                return rating / 5.0f * 10f;
                
            return null;
        }
        
        /// <summary>
        /// 增强的标题清理方法
        /// </summary>
        private string CleanTitleEnhanced(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            
            // 移除编号
            title = Regex.Replace(title, @"^[A-Z]+-\d+\s*", "", RegexOptions.IgnoreCase);
            // 移除常见后缀
            title = Regex.Replace(title, @"\s*-.*?(javbus|javdb).*$", "", RegexOptions.IgnoreCase);
            // 移除多余空格
            title = Regex.Replace(title, @"\s+", " ").Trim();
            
            return title;
        }
    }
}
