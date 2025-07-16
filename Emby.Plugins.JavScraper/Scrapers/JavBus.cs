using Emby.Plugins.JavScraper.Http;
using HtmlAgilityPack;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using System.IO; // Added for Path.GetFileName

namespace Emby.Plugins.JavScraper.Scrapers
{
    /// <summary>
    /// https://www.javbus.com/BIJN-172
    /// </summary>
    public class JavBus : AbstractScraper
    {
        /// <summary>
        /// 适配器名称
        /// </summary>
        public override string Name => "JavBus";

        /// <summary>
        /// 构造
        /// </summary>
        /// <param name="handler"></param>
        public JavBus(ILogManager logManager)
            : base("https://www.javbus.com/", logManager.CreateLogger<JavBus>())
        {
        }

        /// <summary>
        /// 重写BaseUrl设置，添加JavBus特殊的配置 - 速度优化版
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
                    
                // 创建专门为JavBus配置的HttpClient，绕过年龄验证 - 速度优化版
                client = new HttpClientEx(client =>
                {
                    client.BaseAddress = new Uri(value);
                    client.Timeout = TimeSpan.FromSeconds(15); // 快速超时 - 15秒无响应说明网络连接有问题
                    
                    // 应用基础优化配置
                    ConfigureHttpClientOptimized(client);
                    
                    // JavBus特殊配置
                    ConfigureJavBusSpecialHeaders(client);
                    
                    // 配置年龄验证绕过
                    ConfigureAgeVerificationBypass(client, value);
                });
                
                log?.Info($"JavBus BaseUrl configured with anti-bot optimizations: {value}");
            }
        }
        
        /// <summary>
        /// 配置JavBus特殊请求头
        /// </summary>
        private void ConfigureJavBusSpecialHeaders(HttpClient client)
        {
            // JavBus特定的请求头配置
            client.DefaultRequestHeaders.Remove("Accept-Encoding");
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            
            // 添加JavBus偏好的头部
            client.DefaultRequestHeaders.Remove("Sec-Fetch-Site");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
            
            log?.Debug("JavBus特殊请求头配置完成");
        }
        
        /// <summary>
        /// 配置年龄验证绕过策略 - 增强版
        /// </summary>
        private void ConfigureAgeVerificationBypass(HttpClient client, string baseUrl)
        {
            try
            {
                var baseUri = new Uri(baseUrl);
                var cookieContainer = new System.Net.CookieContainer();
                
                // 添加多重年龄验证cookie - 增强版
                var ageCookies = new[]
                {
                    new { Name = "existmag", Value = "all" },
                    new { Name = "age_verified", Value = "1" },
                    new { Name = "over18", Value = "1" },
                    new { Name = "adultcont", Value = "1" },
                    new { Name = "mad", Value = "0" },
                    new { Name = "verify", Value = "1" },
                    new { Name = "age_confirmation", Value = "true" },
                    new { Name = "adult_verified", Value = "1" },
                    new { Name = "confirm_age", Value = "yes" },
                    new { Name = "javbus_age_check", Value = "passed" }
                };
                
                foreach (var cookieInfo in ageCookies)
                {
                    cookieContainer.Add(new System.Net.Cookie(cookieInfo.Name, cookieInfo.Value, "/", baseUri.Host));
                }
                
                // 获取HttpClientHandler并设置cookie
                var handler = client.GetType().GetField("_handler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(client) as HttpClientHandler;
                if (handler != null)
                {
                    handler.CookieContainer = cookieContainer;
                    handler.UseCookies = true;
                    handler.MaxConnectionsPerServer = 12; // 提升并发连接数
                    handler.UseProxy = false; // 禁用代理检测，提升速度
                    // AutomaticDecompression现在在ProxyHttpClientHandler中统一设置

                    log?.Info($"JavBus年龄验证绕过配置完成，设置了{ageCookies.Length}个cookie");
                }
            }
            catch (Exception ex)
            {
                log?.Warn($"Failed to configure age verification bypass for JavBus: {ex.Message}");
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
        /// 改进的HTML获取方法，处理编码和验证问题 - 增强年龄验证绕过版
        /// </summary>
        /// <param name="requestUri"></param>
        /// <returns></returns>
        public override async Task<HtmlDocument> GetHtmlDocumentAsync(string requestUri)
        {
            try
            {
                // 智能URL处理：检查是否已经是完整URL
                string fullUrl;
                if (requestUri.StartsWith("http://") || requestUri.StartsWith("https://"))
                {
                    // 已经是完整URL，直接使用（如DMM链接）
                    fullUrl = requestUri;
                    log?.Info($"JavBus: 使用完整URL: {fullUrl}");
                }
                else
                {
                    // 相对路径，与BaseUrl拼接
                    fullUrl = BaseUrl.TrimEnd('/') + requestUri;
                    log?.Info($"JavBus: 从BaseUrl构建URL: {fullUrl}");
                }

                // 使用增强的HTTP请求方法
                var response = await SafeHttpGetAsync(fullUrl);
                if (!response.IsSuccessStatusCode)
                {
                    log?.Warn($"JavBus: HTTP request failed with status: {response.StatusCode}");
                    return null;
                }

                // 直接读取字符串内容，让HttpClient自动处理压缩和编码
                string html = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(html))
                {
                    log?.Warn($"JavBus: Received empty response");
                    return null;
                }

                log?.Info($"JavBus: Successfully received HTML content, length: {html.Length}");

                // 检查年龄验证并进行增强绕过
                html = await HandleAgeVerificationEnhanced(html, requestUri);
                if (html == null)
                {
                    return null;
                }

                // 检查是否是错误页面
                var lowerHtml = html.ToLower();
                if (lowerHtml.Contains("404") || lowerHtml.Contains("not found") || lowerHtml.Contains("error"))
                {
                    log?.Warn($"JavBus: Detected error page");
                    return null;
                }

                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                
                // 验证文档是否正确加载
                if (doc.DocumentNode == null)
                {
                    log?.Warn($"JavBus: Failed to parse HTML document");
                    return null;
                }

                return doc;
            }
            catch (Exception ex)
            {
                log?.Error($"JavBus: Failed to get HTML from {requestUri}: {ex.Message}");
                return null;
            }
        }
        

        
        /// <summary>
        /// 增强的年龄验证处理方法
        /// </summary>
        private async Task<string> HandleAgeVerificationEnhanced(string html, string requestUri)
        {
            var lowerHtml = html.ToLower();
            if (!lowerHtml.Contains("age") || !(lowerHtml.Contains("verify") || lowerHtml.Contains("confirm") || 
                lowerHtml.Contains("over") || lowerHtml.Contains("adult") || lowerHtml.Contains("18+")))
            {
                return html; // 没有年龄验证
            }
            
            // 检测到年龄验证，启动增强绕过策略
            log?.Info($"JavBus: 检测到年龄验证页面，启动增强绕过策略...");
            
            // 记录部分内容用于调试
            var sample = html.Length > 500 ? html.Substring(0, 500) : html;
            log?.Debug($"JavBus: Age verification page sample: {sample}");
            
            try
            {
                // 增强的年龄验证绕过策略 - 速度优化版
                await Task.Delay(random.Value.Next(200, 500)); // 减少随机延迟
                
                // 方法1：模拟真实浏览器访问首页后再访问目标页面
                log?.Debug($"JavBus: 模拟访问首页建立会话...");
                var homeResponse = await SafeHttpGetAsync("/");
                await Task.Delay(random.Value.Next(300, 600)); // 减少延迟
                
                // 方法2：直接访问年龄验证确认接口
                var verifyUrls = new[] 
                {
                    "/age_verification/confirm",
                    "/verify",
                    $"{requestUri}?confirm=1",
                    $"{requestUri}?age_verified=true"
                };

                foreach (var verifyUrl in verifyUrls)
                {
                    try
                    {
                        log?.Debug($"JavBus: 尝试验证接口: {verifyUrl}");
                        var verifyResponse = await SafeHttpGetAsync(verifyUrl, BaseUrl);
                        if (verifyResponse.IsSuccessStatusCode)
                        {
                            log?.Debug($"JavBus: 验证接口响应: {verifyResponse.StatusCode}");
                        }
                        await Task.Delay(random.Value.Next(100, 300)); // 大幅减少随机间隔
                    }
                    catch (Exception ex)
                    {
                        log?.Debug($"JavBus: 验证接口失败: {ex.Message}");
                    }
                }

                // 方法3：模拟POST提交年龄验证
                try
                {
                    var postData = new Dictionary<string, string>
                    {
                        {"age_verified", "true"},
                        {"confirm", "1"},
                        {"over18", "1"},
                        {"redirect", BaseUrl + requestUri}
                    };

                    var formContent = new FormUrlEncodedContent(postData);
                    
                    // 模拟表单提交的完整请求头
                    if (client?.GetClient() != null)
                    {
                        client.GetClient().DefaultRequestHeaders.Remove("Origin");
                        client.GetClient().DefaultRequestHeaders.Add("Origin", BaseUrl.TrimEnd('/'));
                        client.GetClient().DefaultRequestHeaders.Remove("Referer");
                        client.GetClient().DefaultRequestHeaders.Add("Referer", BaseUrl);
                    }
                    
                    var postResponse = await client.PostAsync("/age_verification", formContent);
                    log?.Debug($"JavBus: POST验证响应: {postResponse.StatusCode}");
                    await Task.Delay(random.Value.Next(200, 400)); // 减少延迟
                }
                catch (Exception ex)
                {
                    log?.Debug($"JavBus: POST验证失败: {ex.Message}");
                }

                // 方法4：重新访问目标页面
                log?.Debug($"JavBus: 重新访问目标页面...");
                var finalResponse = await SafeHttpGetAsync(requestUri, BaseUrl);
                if (finalResponse.IsSuccessStatusCode)
                {
                    var finalHtml = await finalResponse.Content.ReadAsStringAsync();
                    
                    log?.Debug($"JavBus: 重新访问后页面长度: {finalHtml.Length} 字符");
                    
                    // 检查是否成功绕过年龄验证
                    var finalLower = finalHtml.ToLower();

                    // 检查是否仍然是年龄验证页面
                    bool hasAgeVerification = finalLower.Contains("age verification") || finalLower.Contains("年龄验证");

                    // 检查是否是错误页面
                    bool hasError = finalLower.Contains("404") || finalLower.Contains("not found") || finalLower.Contains("error");

                    // 使用测试工具验证的严格检查逻辑
                    log?.Debug($"JavBus: 年龄验证检查 - hasAgeVerification: {hasAgeVerification}, hasError: {hasError}, length: {finalHtml.Length}");

                    // 基于测试工具的严格验证：如果仍有年龄验证或页面太短，则失败
                    if (hasAgeVerification || finalHtml.Length < 5000)
                    {
                        log?.Warn($"JavBus: 年龄验证绕过失败，页面长度: {finalHtml.Length}");

                        // 显示页面片段用于调试
                        var preview = finalHtml.Length > 500 ? finalHtml.Substring(0, 500) : finalHtml;
                        log?.Debug($"JavBus: 页面预览: {preview.Replace("\n", " ").Replace("\r", "")}");
                        return null;
                    }

                    // 如果没有错误且页面长度合理，则成功
                    if (!hasError)
                    {
                        log?.Info($"JavBus: 年龄验证绕过成功! 页面长度: {finalHtml.Length}");
                        return finalHtml;
                    }
                    else
                    {
                        log?.Warn($"JavBus: 页面包含错误内容，绕过失败");
                        return null;
                    }
                }
                
                log?.Warn($"JavBus: 年龄验证绕过失败");
                return null;
            }
            catch (Exception ex)
            {
                log?.Error($"JavBus: 年龄验证绕过过程中出错: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取列表
        /// </summary>
        /// <param name="key">关键字</param>
        /// <returns></returns>
        protected override async Task<List<JavVideoIndex>> DoQyery(List<JavVideoIndex> ls, string key)
        {
            log?.Info($"JavBus DoQuery: 开始搜索关键字: {key}");
            log?.Info($"JavBus DoQuery: 当前BaseUrl: {BaseUrl}");

            // 检查BaseUrl是否正确设置
            if (string.IsNullOrEmpty(BaseUrl))
            {
                log?.Error($"JavBus DoQuery: BaseUrl未设置！");
                return ls;
            }

            //https://www.javbus.cloud/search/33&type=1
            //https://www.javbus.cloud/uncensored/search/33&type=0&parent=uc
            var searchUrl = $"/search/{key}&type=1";
            var fullUrl = BaseUrl.TrimEnd('/') + searchUrl;
            log?.Info($"JavBus DoQuery: 构建完整搜索URL: {fullUrl}");

            log?.Debug($"JavBus DoQuery: 调用GetHtmlDocumentAsync获取搜索页面");
            var doc = await GetHtmlDocumentAsync(searchUrl);

            if (doc != null)
            {
                log?.Info($"JavBus DoQuery: ✅ 成功获取搜索页面HTML，开始解析");
                log?.Debug($"JavBus DoQuery: HTML文档节点数: {doc.DocumentNode?.ChildNodes?.Count ?? 0}");

                int beforeCount = ls.Count;
                ParseIndex(ls, doc);
                int afterCount = ls.Count;
                int foundCount = afterCount - beforeCount;

                log?.Info($"JavBus DoQuery: ✅ 解析完成，本次找到 {foundCount} 个结果，总计 {afterCount} 个结果");

                if (foundCount > 0)
                {
                    log?.Debug($"JavBus DoQuery: 找到的结果详情:");
                    for (int i = beforeCount; i < afterCount && i < beforeCount + 5; i++)
                    {
                        var item = ls[i];
                        log?.Debug($"JavBus DoQuery:   [{i - beforeCount + 1}] {item.Num} - {item.Title}");
                    }
                    if (foundCount > 5)
                    {
                        log?.Debug($"JavBus DoQuery:   ... 还有 {foundCount - 5} 个结果");
                    }
                }
            }
            else
            {
                log?.Error($"JavBus DoQuery: ❌ 获取搜索页面HTML失败，URL: {fullUrl}");
                log?.Debug($"JavBus DoQuery: GetHtmlDocumentAsync返回null，可能原因：网络错误、年龄验证失败、页面不存在");
                return ls;
            }

            //判断是否有 无码的影片
            var node = doc.DocumentNode.SelectSingleNode("//a[contains(@href,'/uncensored/search/')]");
            if (node != null)
            {
                var t = node.InnerText;
                var ii = t.Split('/');
                //没有
                if (ii.Length > 2 && ii[1].Trim().StartsWith("0"))
                    return ls;
            }

            doc = await GetHtmlDocumentAsync($"/uncensored/search/{key}&type=1");
            ParseIndex(ls, doc);

            // 添加精确匹配逻辑
            if (ls.Any())
            {
                // 首先尝试精确匹配
                var exactMatches = ls.Where(i =>
                    string.Equals(i.Num, key, StringComparison.OrdinalIgnoreCase)).ToList();

                if (exactMatches.Any())
                {
                    log?.Info($"JavBus DoQuery: Found {exactMatches.Count} exact matches for {key}");
                    ls.Clear();
                    ls.AddRange(exactMatches);
                }
                else
                {
                    log?.Info($"JavBus DoQuery: No exact matches, keeping all {ls.Count} results for {key}");
                }
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
                log?.Warn("JavBus ParseIndex: Document is null");
                return ls;
            }

            // JavBus最新HTML结构的选择器
            var selectors = new[]
            {
                // 主要搜索结果选择器
                "//a[@class='movie-box']",
                "//div[@class='movie-box']//a",
                "//div[contains(@class,'movie-box')]//a",
                
                // 网格布局选择器
                "//div[@id='waterfall']//a[@class='movie-box']",
                "//div[@id='waterfall']//div//a",
                "//div[@id='waterfall']//a[contains(@class,'movie-box')]",
                
                // 列表项选择器
                "//div[@class='item']//a",
                "//div[contains(@class,'item')]//a",
                "//div[@class='video-item']//a",
                "//div[contains(@class,'video-item')]//a",
                
                // 搜索结果页面选择器
                "//div[@class='row']//div[@class='col-sm-3']//a",
                "//div[contains(@class,'col-')]//a[contains(@class,'movie-box')]",
                "//div[contains(@class,'grid')]//a",
                "//div[contains(@class,'box')]//a",
                
                // 备用通用选择器
                "//a[contains(@href,'/') and img[@src]]",  // 包含图片的链接
                "//a[contains(@href,'/') and div[@class='photo-frame']]", // 包含photo-frame的链接
                "//a[img[contains(@src,'cover') or contains(@src,'thumb')]]", // 包含封面或缩略图的链接
                "//a[contains(@href,'/') and not(contains(@href,'javascript')) and not(contains(@href,'#'))]", // 普通链接但排除JS和锚点
                
                // 最宽泛的选择器（作为最后手段）
                "//a[img]" // 任何包含图片的链接
            };

            HtmlNodeCollection nodes = null;
            string usedSelector = null;
            
            foreach (var selector in selectors)
            {
                nodes = doc.DocumentNode.SelectNodes(selector);
                if (nodes?.Any() == true)
                {
                    // 改进的电影链接过滤逻辑
                    var movieNodes = nodes.Where(n => 
                    {
                        var href = n.GetAttributeValue("href", "");
                        
                        // 基本检查
                        if (string.IsNullOrEmpty(href) || !href.Contains("/"))
                            return false;
                            
                        // 排除明显的非电影链接
                        var excludePatterns = new[]
                        {
                            "javascript", "mailto", "#", "void(0)",
                            "driver-verify", "age-verify", "verify.html",
                            "/doc/", "/help/", "/about/", "/contact/",
                            "/genre/", "/actresses/", "/directors/",
                            "/uncensored$", "/search/", "/page/",
                            "/login", "/register", "/logout",
                            ".css", ".js", ".ico", ".png", ".jpg", ".gif"
                        };
                        
                        foreach (var pattern in excludePatterns)
                        {
                            if (href.Contains(pattern))
                                return false;
                        }
                        
                        // 检查是否有图片子元素（电影链接通常包含图片）
                        var hasImage = n.SelectSingleNode(".//img") != null;
                        
                        // 更宽松的电影链接匹配
                        var looksLikeMovie = 
                            Regex.IsMatch(href, @"/[A-Za-z]+[-_]?\d+") || // 标准番号格式
                            Regex.IsMatch(href, @"/\d+[A-Za-z]+") || // 数字开头格式
                            Regex.IsMatch(href, @"/[A-Za-z]+\d+") || // 字母开头格式
                            Regex.IsMatch(href, @"/[a-zA-Z0-9]{5,}$") || // 5位以上字母数字结尾
                            (hasImage && href.Length > 10 && !href.EndsWith("/")); // 有图片且链接合理长度
                        
                        return looksLikeMovie;
                    }).ToList();
                    
                    if (movieNodes.Any())
                    {
                        nodes = new HtmlNodeCollection(null);
                        foreach (var node in movieNodes)
                        {
                            nodes.Add(node);
                        }
                        usedSelector = selector;
                        log?.Info($"JavBus ParseIndex: Found {nodes.Count} movie nodes using selector: {selector}");
                        break;
                    }
                }
            }

            if (nodes?.Any() != true)
            {
                // 如果还是找不到，记录HTML内容用于调试
                var htmlLength = doc.DocumentNode.InnerHtml?.Length ?? 0;
                log?.Warn($"JavBus ParseIndex: No movie nodes found with any selector. HTML length: {htmlLength}");
                
                // 记录部分HTML内容用于调试（取前500个字符，处理编码问题）
                var htmlSample = "";
                try
                {
                    var rawHtml = doc.DocumentNode.InnerHtml ?? "";
                    if (rawHtml.Length > 0)
                    {
                        // 安全地提取HTML样本，避免编码问题
                        var sampleLength = Math.Min(500, rawHtml.Length);
                        htmlSample = rawHtml.Substring(0, sampleLength);
                        
                        // 如果包含不可打印字符，用安全的方式显示
                        if (htmlSample.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
                        {
                            // 替换控制字符为可见字符
                            htmlSample = System.Text.RegularExpressions.Regex.Replace(htmlSample, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "?");
                        }
                    }
                }
                catch
                {
                    htmlSample = "[HTML content could not be safely extracted]";
                }
                log?.Info($"JavBus ParseIndex: HTML sample: {htmlSample}");
                
                // 额外记录更多调试信息
                var allLinks = doc.DocumentNode.SelectNodes("//a[@href]");
                log?.Info($"JavBus ParseIndex: Total links found: {allLinks?.Count ?? 0}");
                
                if (allLinks?.Any() == true)
                {
                    var sampleLinks = allLinks.Take(10).Select(n => n.GetAttributeValue("href", "")).ToArray();
                    log?.Info($"JavBus ParseIndex: Sample links: {string.Join(", ", sampleLinks)}");
                }
                
                return ls;
            }

            log?.Info($"JavBus ParseIndex: Processing {nodes.Count} nodes with selector: {usedSelector}");

            foreach (var node in nodes)
            {
                var url = node.GetAttributeValue("href", null);
                if (string.IsNullOrWhiteSpace(url))
                {
                    log?.Debug("JavBus ParseIndex: Node has no href attribute");
                    continue;
                }
                
                var m = new JavVideoIndex() { Provider = Name, Url = url };

                // 尝试多种图片选择器
                var imgSelectors = new[]
                {
                    ".//div[@class='photo-frame']//img",
                    ".//div[contains(@class,'photo-frame')]//img",
                    ".//img[@class='movie']",
                    ".//img[contains(@class,'movie')]",
                    ".//img",
                    ".//div[@class='photo-info']//img",
                    ".//div[contains(@class,'photo')]//img",
                    ".//div[contains(@class,'cover')]//img",
                    ".//div[contains(@class,'thumbnail')]//img"
                };

                foreach (var imgSelector in imgSelectors)
                {
                    var img = node.SelectSingleNode(imgSelector);
                if (img != null)
                {
                    m.Cover = img.GetAttributeValue("src", null);
                        if (string.IsNullOrEmpty(m.Cover))
                            m.Cover = img.GetAttributeValue("data-original", null);
                        if (string.IsNullOrEmpty(m.Cover))
                            m.Cover = img.GetAttributeValue("data-src", null);
                        if (string.IsNullOrEmpty(m.Cover))
                            m.Cover = img.GetAttributeValue("data-lazy", null);
                        
                    m.Title = img.GetAttributeValue("title", null);
                        if (string.IsNullOrEmpty(m.Title))
                            m.Title = img.GetAttributeValue("alt", null);
                        
                        if (!string.IsNullOrEmpty(m.Cover))
                        {
                            log?.Debug($"JavBus ParseIndex: Found cover using selector: {imgSelector}");
                            break;
                        }
                    }
                }

                // 尝试多种日期和番号选择器
                var infoSelectors = new[]
                {
                    ".//date",
                    ".//div[@class='info']//date",
                    ".//div[contains(@class,'info')]//date",
                    ".//span[@class='date']",
                    ".//div[@class='date']",
                    ".//span[contains(@class,'uid')]",
                    ".//div[contains(@class,'uid')]",
                    ".//span[contains(@class,'code')]",
                    ".//div[contains(@class,'code')]"
                };

                foreach (var infoSelector in infoSelectors)
                {
                    var infoNodes = node.SelectNodes(infoSelector);
                    if (infoNodes?.Any() == true)
                    {
                        foreach (var infoNode in infoNodes)
                        {
                            var text = infoNode.InnerText?.Trim();
                            if (!string.IsNullOrEmpty(text))
                            {
                                // 如果看起来像番号（包含字母和数字）
                                if (Regex.IsMatch(text, @"[A-Za-z]+[-_]?\d+|\d+[A-Za-z]+"))
                                {
                                    if (string.IsNullOrEmpty(m.Num))
                                        m.Num = text;
                                }
                                // 如果看起来像日期（包含-或/）
                                else if (text.Contains("-") || text.Contains("/"))
                                {
                                    if (string.IsNullOrEmpty(m.Date))
                                        m.Date = text;
                                }
                            }
                        }
                    }
                }

                // 尝试从标题中提取番号
                if (string.IsNullOrEmpty(m.Num) && !string.IsNullOrEmpty(m.Title))
                {
                    var titleMatch = Regex.Match(m.Title, @"([A-Za-z]+[-_]?\d+)");
                    if (titleMatch.Success)
                    {
                        m.Num = titleMatch.Groups[1].Value;
                        log?.Debug($"JavBus ParseIndex: Extracted Num from title: {m.Num}");
                    }
                }

                // 尝试从URL中提取番号作为最后手段
                if (string.IsNullOrEmpty(m.Num))
                {
                    var urlMatch = Regex.Match(url, @"/([A-Za-z]+[-_]?\d+)/?$");
                    if (urlMatch.Success)
                    {
                        m.Num = urlMatch.Groups[1].Value;
                        log?.Debug($"JavBus ParseIndex: Extracted Num from URL: {m.Num}");
                    }
                }

                if (string.IsNullOrWhiteSpace(m.Num))
                {
                    log?.Debug($"JavBus ParseIndex: No Num found for URL: {url}");
                    continue;
                }

                log?.Debug($"JavBus ParseIndex: Found movie - Num: {m.Num}, Title: {m.Title}, URL: {url}");
                ls.Add(m);
            }

            log?.Info($"JavBus ParseIndex: Total movies found: {ls.Count}");
            return ls;
        }

        /// <summary>
        /// 获取完整的视频信息 - 融合测试工具验证的增强逻辑
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public override async Task<JavVideo> Get(string url)
        {
            try
            {
                log?.Info($"JavBus Get: 🎬 开始刮削详细信息 - URL: {url}");
                log?.Debug($"JavBus Get: 当前BaseUrl: {BaseUrl}");

                // 智能URL处理 - 支持完整URL和相对路径
                string requestPath;
                if (url.StartsWith("http"))
                {
                    log?.Debug($"JavBus Get: 检测到完整URL，提取路径部分");
                    var uri = new Uri(url);
                    requestPath = uri.PathAndQuery;
                    log?.Debug($"JavBus Get: 提取的路径: {requestPath}");
                }
                else if (url.StartsWith("/"))
                {
                    log?.Debug($"JavBus Get: 检测到相对路径，直接使用");
                    requestPath = url;
                }
                else
                {
                    log?.Debug($"JavBus Get: 检测到番号，添加前缀");
                    requestPath = "/" + url;
                }

                log?.Info($"JavBus Get: 📡 使用请求路径: {requestPath}");
                log?.Debug($"JavBus Get: 调用GetHtmlDocumentAsync获取页面内容");

                var doc = await GetHtmlDocumentAsync(requestPath);
                if (doc == null)
                {
                    log?.Error($"JavBus Get: ❌ 无法获取HTML文档: {url}");
                    log?.Debug($"JavBus Get: GetHtmlDocumentAsync返回null，可能原因：网络错误、年龄验证失败、页面不存在");
                    return null;
                }

                log?.Info($"JavBus Get: ✅ 成功获取HTML文档，开始解析内容");
                log?.Debug($"JavBus Get: HTML文档节点数: {doc.DocumentNode?.ChildNodes?.Count ?? 0}");

                // 增强的内容解析 - 使用测试验证的选择器
                var movie = new JavVideo() { Provider = Name, Url = url };
                log?.Debug($"JavBus Get: 创建JavVideo对象，Provider: {Name}");

                // 增强的标题提取 - 使用多种选择器
                log?.Debug($"JavBus Get: 🏷️ 开始提取标题");
                var titleSelectors = new[]
                {
                    "//h3[contains(@class, 'title')]",
                    "//div[@class='container']/h3",
                    "//div[contains(@class, 'info')]//h3",
                    "//title"
                };

                bool titleFound = false;
                for (int i = 0; i < titleSelectors.Length; i++)
                {
                    var selector = titleSelectors[i];
                    log?.Debug($"JavBus Get: 尝试标题选择器 [{i + 1}/{titleSelectors.Length}]: {selector}");

                    var titleNode = doc.DocumentNode.SelectSingleNode(selector);
                    if (titleNode != null && !string.IsNullOrEmpty(titleNode.InnerText?.Trim()))
                    {
                        var rawTitle = titleNode.InnerText?.Trim();
                        log?.Debug($"JavBus Get: 找到原始标题: {rawTitle}");

                        var title = CleanTitleEnhanced(rawTitle);
                        log?.Debug($"JavBus Get: 清理后标题: {title}");

                        if (!string.IsNullOrEmpty(title) && !title.ToLower().Contains("javbus") &&
                            !title.ToLower().Contains("age verification"))
                        {
                            movie.Title = title;
                            log?.Info($"JavBus Get: ✅ 成功提取标题: {title}");
                            titleFound = true;
                            log?.Debug($"JavBus: 标题提取成功: {movie.Title}");
                            break;
                        }
                    }
                }

                // 增强的信息提取 - 多种方式获取元数据
                var container = doc.DocumentNode.SelectSingleNode("//div[@class='container']/h3/..");
                if (container != null)
                {
                    var dic = ExtractMetadataEnhanced(container);
                    
                    // 使用增强的元数据提取
                    movie.Num = GetValueEnhanced(dic, "識別碼");
                    movie.Date = GetValueEnhanced(dic, "發行日期");
                    movie.Runtime = GetValueEnhanced(dic, "長度");
                    movie.Maker = GetValueEnhanced(dic, "發行商");
                    movie.Studio = GetValueEnhanced(dic, "製作商");
                    movie.Set = GetValueEnhanced(dic, "系列");
                    movie.Director = GetValueEnhanced(dic, "導演");
                }

                // 增强的演员提取
                var actorSelectors = new[]
                {
                    "//div[@class='star-name']//a",
                    "//span[@class='genre']//a[contains(@href, '/star/')]",
                    "//div[contains(@class, 'star')]//a",
                    "//a[contains(@href, '/star/')]"
                };

                movie.Actors = new List<string>();
                foreach (var selector in actorSelectors)
                {
                    var actorNodes = doc.DocumentNode.SelectNodes(selector);
                    if (actorNodes != null)
                    {
                        foreach (var node in actorNodes.Take(5))
                        {
                            var actorName = CleanTitleEnhanced(node.InnerText);
                            if (!string.IsNullOrEmpty(actorName) && !movie.Actors.Contains(actorName))
                            {
                                movie.Actors.Add(actorName);
                            }
                        }
                        if (movie.Actors.Any()) break;
                    }
                }
                log?.Debug($"JavBus: 演员提取完成: {string.Join(", ", movie.Actors)}");

                // 增强的标签提取
                var tagSelectors = new[]
                {
                    "//span[@class='genre']//a[not(contains(@href, '/star/'))]",
                    "//div[@class='genre']//a",
                    "//span[contains(@class, 'genre')]//a",
                    "//a[contains(@href, '/genre/')]"
                };

                movie.Genres = new List<string>();
                foreach (var selector in tagSelectors)
                {
                    var tagNodes = doc.DocumentNode.SelectNodes(selector);
                    if (tagNodes != null)
                    {
                        foreach (var node in tagNodes.Take(10))
                        {
                            var tag = CleanTitleEnhanced(node.InnerText);
                            if (!string.IsNullOrEmpty(tag) && !movie.Genres.Contains(tag))
                            {
                                movie.Genres.Add(tag);
                            }
                        }
                        if (movie.Genres.Any()) break;
                    }
                }
                log?.Debug($"JavBus: 标签提取完成: {string.Join(", ", movie.Genres)}");

                // 增强的图片提取 - 优先获取高质量大图
                log?.Info($"JavBus Get: 🖼️ 开始提取图片");
                log?.Debug($"JavBus Get: 调用ExtractImagesEnhanced方法");

                movie.Samples = ExtractImagesEnhanced(doc);
                int imageCount = movie.Samples?.Count ?? 0;

                if (imageCount > 0)
                {
                    log?.Info($"JavBus Get: ✅ 图片提取完成，共 {imageCount} 张");
                    log?.Debug($"JavBus Get: 图片列表前5张:");
                    for (int i = 0; i < Math.Min(5, imageCount); i++)
                    {
                        log?.Debug($"JavBus Get:   [{i + 1}] {movie.Samples[i]}");
                    }
                    if (imageCount > 5)
                    {
                        log?.Debug($"JavBus Get:   ... 还有 {imageCount - 5} 张图片");
                    }
                }
                else
                {
                    log?.Warn($"JavBus Get: ⚠️ 未提取到任何图片");
                }

                // 获取封面大图
                var coverNode = doc.DocumentNode.SelectSingleNode("//a[@class='bigImage']");
                if (coverNode != null)
                {
                    var coverUrl = coverNode.GetAttributeValue("href", "");
                    if (!string.IsNullOrEmpty(coverUrl))
                    {
                        if (coverUrl.StartsWith("//")) coverUrl = "https:" + coverUrl;
                        else if (coverUrl.StartsWith("/")) coverUrl = BaseUrl.TrimEnd('/') + coverUrl;
                        movie.Cover = coverUrl;
                        log?.Debug($"JavBus: 封面提取成功: {movie.Cover}");
                    }
                }

                // 获取剧情 (保持原有逻辑)
                movie.Plot = await GetDmmPlot(movie.Num);
                
                // 清理标题中的番号
                if (!string.IsNullOrWhiteSpace(movie.Num) && movie.Title?.StartsWith(movie.Num, StringComparison.OrdinalIgnoreCase) == true)
                {
                    movie.Title = movie.Title.Substring(movie.Num.Length).Trim();
                }

                // 最终结果验证和日志
                log?.Info($"JavBus Get: 🎉 刮削完成，开始结果验证");
                log?.Debug($"JavBus Get: 最终结果统计:");
                log?.Debug($"JavBus Get:   标题: {movie.Title ?? "未获取"}");
                log?.Debug($"JavBus Get:   番号: {movie.Num ?? "未获取"}");
                log?.Debug($"JavBus Get:   演员: {movie.Actors?.Count ?? 0}人");
                log?.Debug($"JavBus Get:   标签: {movie.Genres?.Count ?? 0}个");
                log?.Debug($"JavBus Get:   图片: {movie.Samples?.Count ?? 0}张");
                log?.Debug($"JavBus Get:   封面: {movie.Cover ?? "未获取"}");

                bool hasTitle = !string.IsNullOrEmpty(movie.Title);
                bool hasImages = movie.Samples?.Count > 0;
                bool hasBasicInfo = hasTitle || hasImages;

                if (hasBasicInfo)
                {
                    log?.Info($"JavBus Get: ✅ 刮削成功 - 标题: {movie.Title}, 演员: {movie.Actors?.Count ?? 0}人, 标签: {movie.Genres?.Count ?? 0}个, 图片: {movie.Samples?.Count ?? 0}张");
                }
                else
                {
                    log?.Warn($"JavBus Get: ⚠️ 刮削结果不完整 - 缺少基本信息（标题和图片）");
                }

                return movie;
            }
            catch (Exception ex)
            {
                log?.Error($"JavBus Get: ❌ 刮削异常: {ex.Message}");
                log?.Debug($"JavBus Get: 异常堆栈: {ex.StackTrace}");
                return null;
            }
        }
        
        /// <summary>
        /// 增强的元数据提取方法
        /// </summary>
        private Dictionary<string, string> ExtractMetadataEnhanced(HtmlNode container)
        {
            var dic = new Dictionary<string, string>();
            var nodes = container.SelectNodes(".//span[@class='header']");
            
            if (nodes != null)
            {
                foreach (var n in nodes)
                {
                    var next = n.NextSibling;
                    while (next != null && string.IsNullOrWhiteSpace(next.InnerText))
                        next = next.NextSibling;
                    if (next != null)
                        dic[n.InnerText.Trim()] = next.InnerText.Trim();
                }
            }
            
            return dic;
        }
        
        /// <summary>
        /// 增强的值获取方法
        /// </summary>
        private string GetValueEnhanced(Dictionary<string, string> dic, string key)
        {
            return dic.Where(o => o.Key.Contains(key)).Select(o => o.Value).FirstOrDefault();
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
        
        /// <summary>
        /// 增强的图片提取方法 - 融合测试工具验证的逻辑
        /// </summary>
        private List<string> ExtractImagesEnhanced(HtmlDocument doc)
        {
            var images = new List<string>();
            
            // 第一优先级：获取高清封面大图 (bigImage链接)
            var bigImageLinks = doc.DocumentNode.SelectNodes("//a[@class='bigImage']");
            if (bigImageLinks != null)
            {
                foreach (var link in bigImageLinks.Take(3))
                {
                    var href = link.GetAttributeValue("href", "");
                    if (!string.IsNullOrEmpty(href))
                    {
                        if (href.StartsWith("//")) href = "https:" + href;
                        else if (href.StartsWith("/")) href = BaseUrl.TrimEnd('/') + href;
                        
                        if (href.StartsWith("http") && !images.Contains(href))
                        {
                            images.Add(href);
                            log?.Debug($"JavBus: 找到高清封面: {Path.GetFileName(href)}");
                        }
                    }
                }
            }
            
            // 第二优先级：获取样品图片链接 (sample-box链接)
            var sampleLinks = doc.DocumentNode.SelectNodes("//a[@class='sample-box']");
            if (sampleLinks != null)
            {
                foreach (var link in sampleLinks.Take(10))
                {
                    var href = link.GetAttributeValue("href", "");
                    if (!string.IsNullOrEmpty(href))
                    {
                        if (href.StartsWith("//")) href = "https:" + href;
                        else if (href.StartsWith("/")) href = BaseUrl.TrimEnd('/') + href;
                        
                        if (href.StartsWith("http") && !images.Contains(href))
                        {
                            images.Add(href);
                            log?.Debug($"JavBus: 找到样品图片: {Path.GetFileName(href)}");
                        }
                    }
                }
            }
            
            // 第三优先级：如果上面都没找到，尝试直接的img标签 (作为备用)
            if (images.Count == 0)
            {
                log?.Debug($"JavBus: 未找到链接图片，尝试直接img标签...");
                var imgSelectors = new[]
                {
                    "//div[@class='photo-frame']//img",
                    "//img[contains(@src, 'cover')]",
                    "//img[contains(@src, 'sample')]",
                    "//img[contains(@src, '.jpg')]"
                };

                foreach (var selector in imgSelectors)
                {
                    var imgNodes = doc.DocumentNode.SelectNodes(selector);
                    if (imgNodes != null)
                    {
                        foreach (var img in imgNodes.Take(5))
                        {
                            var src = img.GetAttributeValue("src", "");
                            if (!string.IsNullOrEmpty(src))
                            {
                                if (src.StartsWith("//")) src = "https:" + src;
                                else if (src.StartsWith("/")) src = BaseUrl.TrimEnd('/') + src;
                                
                                if (src.StartsWith("http") && !images.Contains(src))
                                {
                                    images.Add(src);
                                    log?.Debug($"JavBus: 备用图片: {Path.GetFileName(src)}");
                                }
                            }
                        }
                        if (images.Any()) break;
                    }
                }
            }
            
            return images;
        }
    }
}