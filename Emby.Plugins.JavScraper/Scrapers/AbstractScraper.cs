using Emby.Plugins.JavScraper.Http;
using HtmlAgilityPack;

using MediaBrowser.Model.Logging;

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;

namespace Emby.Plugins.JavScraper.Scrapers
{
    /// <summary>
    /// 基础类型 - 增强反爬虫版
    /// </summary>
    public abstract class AbstractScraper : IDisposable
    {
        protected HttpClientEx client;
        protected ILogger log;
        private static NamedLockerAsync locker = new NamedLockerAsync();
        
        // 反爬虫增强配置 - 速度优化版（线程安全）
        protected static readonly ThreadLocal<Random> random = new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));
        protected readonly string[] userAgents = {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/121.0",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/120.0",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.1 Safari/605.1.15"
        };
        
        protected readonly ConcurrentDictionary<string, DateTime> lastRequestTime = new ConcurrentDictionary<string, DateTime>();
        protected readonly SemaphoreSlim requestSemaphore = new SemaphoreSlim(6, 6); // 全局并发限制
        
        /// <summary>
        /// 是否已释放资源
        /// </summary>
        protected bool disposed = false;

        /// <summary>
        /// 适配器名称
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// 默认的基础URL
        /// </summary>
        public string DefaultBaseUrl { get; }

        /// <summary>
        /// 基础URL
        /// </summary>
        private string base_url = null;

        /// <summary>
        /// 基础URL
        /// </summary>
        public virtual string BaseUrl
        {
            get => base_url;
            set
            {
                if (value.IsWebUrl() != true)
                    return;
                if (base_url == value && client != null)
                    return;
                base_url = value;
                client = new HttpClientEx(client => 
                {
                    client.BaseAddress = new Uri(base_url);
                    ConfigureHttpClientOptimized(client); // 应用优化配置
                });
                log?.Info($"BaseUrl: {base_url}");
            }
        }
        
        /// <summary>
        /// 配置HTTP客户端优化设置
        /// </summary>
        protected virtual void ConfigureHttpClientOptimized(HttpClient client)
        {
            client.Timeout = TimeSpan.FromSeconds(15); // 快速超时 - 15秒无响应说明网络连接有问题
            
            // 基础请求头设置 - 模拟真实浏览器
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Accept", 
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            client.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8,ja;q=0.7");
            client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
            client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            client.DefaultRequestHeaders.Add("DNT", "1");
            client.DefaultRequestHeaders.Add("Pragma", "no-cache");
            client.DefaultRequestHeaders.Add("Sec-Ch-Ua", "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"120\", \"Google Chrome\";v=\"120\"");
            client.DefaultRequestHeaders.Add("Sec-Ch-Ua-Mobile", "?0");
            client.DefaultRequestHeaders.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
            client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            
            // 设置初始User-Agent
            RotateUserAgent(client);
        }
        
        /// <summary>
        /// 动态轮换User-Agent
        /// </summary>
        protected void RotateUserAgent(HttpClient client = null)
        {
            var targetClient = client ?? this.client?.GetClient();
            if (targetClient == null) return;
            
            var userAgent = userAgents[random.Value.Next(userAgents.Length)];
            targetClient.DefaultRequestHeaders.Remove("User-Agent");
            targetClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
            log?.Debug($"轮换User-Agent: {userAgent.Substring(0, Math.Min(50, userAgent.Length))}...");
        }
        
        /// <summary>
        /// 智能请求延迟，避免被检测为机器人 - 速度优化版
        /// </summary>
        protected async Task SmartDelay(string domain)
        {
            if (lastRequestTime.TryGetValue(domain, out DateTime lastTime))
            {
                var elapsed = DateTime.Now - lastTime;
                var minDelay = TimeSpan.FromMilliseconds(200 + random.Value.Next(100, 300)); // 0.2-0.5秒随机延迟
                
                if (elapsed < minDelay)
                {
                    var delayTime = minDelay - elapsed;
                    log?.Debug($"智能延迟 {delayTime.TotalMilliseconds:F0}ms 避免限流");
                    await Task.Delay(delayTime);
                }
            }
            lastRequestTime[domain] = DateTime.Now;
        }
        
        /// <summary>
        /// 增强版HTTP请求方法，带反爬虫对策
        /// </summary>
        protected async Task<HttpResponseMessage> SafeHttpGetAsync(string url, string referer = null)
        {
            await requestSemaphore.WaitAsync(); // 全局并发控制
            try
            {
                var uri = new Uri(url);
                await SmartDelay(uri.Host); // 智能延迟
                
                RotateUserAgent(); // 轮换UA
                
                // 设置Referer
                if (client?.GetClient() != null)
                {
                    client.GetClient().DefaultRequestHeaders.Remove("Referer");
                    if (!string.IsNullOrEmpty(referer))
                    {
                        client.GetClient().DefaultRequestHeaders.Add("Referer", referer);
                    }
                    
                    // 添加随机请求头增强真实性
                    client.GetClient().DefaultRequestHeaders.Remove("X-Requested-With");
                    if (random.Value.Next(3) == 0) // 33%概率添加
                    {
                        client.GetClient().DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
                    }
                }
                
                return await client.GetAsync(url);
            }
            finally
            {
                requestSemaphore.Release();
            }
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="base_url">基础URL</param>
        /// <param name="log">日志记录器</param>
        public AbstractScraper(string base_url, ILogger log)
        {
            this.log = log;
            DefaultBaseUrl = base_url;
            BaseUrl = base_url;
        }

        //ABC-00012 --> ABC-012
        protected static Regex regexKey = new Regex("^(?<a>[a-z0-9]{3,5})(?<b>[-_ ]*)(?<c>0{1,2}[0-9]{3,5})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        //7ABC-012  --> ABC-012
        protected static Regex regexKey2 = new Regex("^[0-9][a-z]+[-_a-z0-9]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 展开全部的Key
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        protected virtual List<string> GetAllKeys(string key)
        {
            var ls = new List<string>();

            var m = regexKey2.Match(key);
            if (m.Success)
                ls.Add(key.Substring(1));

            ls.Add(key);

            m = regexKey.Match(key);
            if (m.Success)
            {
                var a = m.Groups["a"].Value;
                var b = m.Groups["b"].Value;
                var c = m.Groups["c"].Value;
                var end = c.TrimStart('0');
                var count = c.Length - end.Length - 1;
                for (int i = 0; i <= count; i++)
                {
                    var em = i > 0 ? new string('0', i) : string.Empty;
                    ls.Add($"{a}{em}{end}");
                    ls.Add($"{a}-{em}{end}");
                    ls.Add($"{a}_{em}{end}");
                }
            }

            if (key.IndexOf('-') > 0)
                ls.Add(key.Replace("-", "_"));
            if (key.IndexOf('_') > 0)
                ls.Add(key.Replace("_", "-"));

            if (ls.Count > 1)
                ls.Add(key.Replace("-", "").Replace("_", ""));

            return ls;
        }

        /// <summary>
        /// 排序
        /// </summary>
        /// <param name="key"></param>
        /// <param name="ls"></param>
        protected virtual void SortIndex(string key, List<JavVideoIndex> ls)
        {
            if (ls?.Any() != true)
                return;

            //返回的多个结果中，第一个未必是最匹配的，需要手工匹配下
            if (ls.Count > 1 && string.Compare(ls[0].Num, key, true) != 0) //多个结果，且第一个不一样
            {
                var m = ls.FirstOrDefault(o => string.Compare(o.Num, key, true) == 0)
                    ?? ls.Select(o => new { m = o, v = LevenshteinDistance.Calculate(o.Num.ToUpper(), key.ToUpper()) }).OrderBy(o => o.v).FirstOrDefault().m;

                ls.Remove(m);
                ls.Insert(0, m);
            }
        }

        /// <summary>
        /// 检查关键字是否符合
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public abstract bool CheckKey(string key);

        /// <summary>
        /// 获取列表
        /// </summary>
        /// <param name="key">关键字</param>
        /// <returns></returns>
        public virtual async Task<List<JavVideoIndex>> Query(string key)
        {
            var ls = new List<JavVideoIndex>();
            if (CheckKey(key) == false)
                return ls;
                
            try
            {
                var keys = GetAllKeys(key);
                foreach (var k in keys)
                {
                    try
                    {
                        await DoQyery(ls, k);
                        if (ls.Any())
                        {
                            var uri = new Uri(base_url);
                            foreach (var r in ls)
                            {
                                try
                                {
                                    r.Url = FixUrl(uri, r.Url);
                                    r.Cover = FixUrl(uri, r.Cover);
                                }
                                catch (Exception ex)
                                {
                                    // URL修复失败时记录日志但继续处理
                                    log?.Debug($"Failed to fix URL for video result: {ex.Message}");
                                }
                            }

                            return ls;
                        }
                    }
                    catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.Message.Contains("timeout"))
                    {
                        // 网络超时，快速退出，不再尝试其他key
                        log?.Error($"网络连接超时，停止刮削: {ex.Message} - 请检查网络连接或启用代理");
                        return ls;
                    }
                    catch (HttpRequestException ex)
                    {
                        // 网络连接失败，快速退出，不再尝试其他key
                        log?.Error($"网络连接失败，停止刮削: {ex.Message} - 请检查网络连接或启用代理");
                        return ls;
                    }
                    catch (Exception ex)
                    {
                        // 其他异常继续尝试下一个key
                        log?.Warn($"Query failed for key '{k}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // 捕获所有未预期的异常，确保Query方法总是能返回结果
                log?.Error($"Unexpected error in Query for key '{key}': {ex.Message}");
            }
            
            return ls;
        }

        /// <summary>
        /// 解析列表
        /// </summary>
        /// <param name="ls"></param>
        /// <param name="doc"></param>
        /// <returns></returns>
        protected abstract Task<List<JavVideoIndex>> DoQyery(List<JavVideoIndex> ls, string key);

        /// <summary>
        /// 解析列表
        /// </summary>
        /// <param name="ls"></param>
        /// <param name="doc"></param>
        /// <returns></returns>
        protected abstract List<JavVideoIndex> ParseIndex(List<JavVideoIndex> ls, HtmlDocument doc);

        /// <summary>
        /// 获取详情
        /// </summary>
        /// <param name="index">地址</param>
        /// <returns></returns>
        public virtual async Task<JavVideo> Get(JavVideoIndex index)
        {
            var r = await Get(index?.Url);
            if (r != null)
            {
                r.OriginalTitle = r.Title;
                try
                {
                    var uri = new Uri(index?.Url ?? r.Url ?? BaseUrl);
                    r.Cover = FixUrl(uri, r.Cover);
                    if (r.Samples?.Any() == true)
                        r.Samples = r.Samples.Select(o => FixUrl(uri, o))
                            .Where(o => o != null)
                            .ToList();
                }
                catch (Exception ex)
                {
                    // URL修复失败时记录日志但继续处理
                    log?.Debug($"Failed to fix URLs for video {r?.Title}: {ex.Message}");
                }
            }
            return r;
        }

        /// <summary>
        /// 补充完整url
        /// </summary>
        /// <param name="base_uri">基础url</param>
        /// <param name="url">url或者路径</param>
        /// <returns></returns>
        protected virtual string FixUrl(Uri base_uri, string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            if (url.IsWebUrl())
                return url;

            try
            {
                return new Uri(base_uri, url).ToString();
            }
            catch (Exception ex)
            {
                // URL构造失败时记录日志但返回null
                log?.Debug($"Failed to construct URL from base '{base_uri}' and relative '{url}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 获取详情
        /// </summary>
        /// <param name="url">地址</param>
        /// <returns></returns>
        public abstract Task<JavVideo> Get(string url);

        /// <summary>
        /// 获取 HtmlDocument
        /// </summary>
        /// <param name="requestUri"></param>
        /// <returns></returns>
        public virtual Task<HtmlDocument> GetHtmlDocumentAsync(string requestUri)
            => GetHtmlDocumentAsync(client, requestUri, log);

        /// <summary>
        /// 获取 HtmlDocument
        /// </summary>
        /// <param name="requestUri"></param>
        /// <returns></returns>
        public static async Task<HtmlDocument> GetHtmlDocumentAsync(HttpClientEx client, string requestUri, ILogger log = default)
        {
            try
            {
                // 检查参数有效性
                if (string.IsNullOrWhiteSpace(requestUri))
                {
                    log?.Error("GetHtmlDocumentAsync: requestUri is null or empty");
                    return null;
                }
                
                if (client == null)
                {
                    log?.Error("GetHtmlDocumentAsync: HttpClientEx is null");
                    return null;
                }
                
                log?.Info($"Requesting URL: {requestUri}");
                var startTime = DateTime.Now;
                var html = await client.GetStringAsync(requestUri);
                var elapsed = DateTime.Now - startTime;

                if (string.IsNullOrWhiteSpace(html) == false)
                {
                    log?.Info($"Successfully received HTML content, length: {html.Length}, elapsed: {elapsed.TotalSeconds:F2}s");
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);
                    return doc;
                }
                else
                {
                    log?.Warn($"Received empty HTML content from: {requestUri}, elapsed: {elapsed.TotalSeconds:F2}s");
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.Message.Contains("timeout"))
            {
                log?.Error($"网络连接超时 (15秒无响应): {requestUri} - 请检查网络连接或启用代理");
            }
            catch (HttpRequestException ex)
            {
                log?.Error($"网络连接失败: {requestUri} - {ex.Message} - 请检查网络连接或启用代理");
            }
            catch (Exception ex)
            {
                log?.Error($"Failed to get HTML from {requestUri}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 获取 HtmlDocument，通过 Post 方法提交
        /// </summary>
        /// <param name="requestUri"></param>
        /// <returns></returns>
        public virtual Task<HtmlDocument> GetHtmlDocumentByPostAsync(string requestUri, Dictionary<string, string> param)
            => GetHtmlDocumentByPostAsync(requestUri, new FormUrlEncodedContent(param));

        /// <summary>
        /// 获取 HtmlDocument，通过 Post 方法提交
        /// </summary>
        /// <param name="requestUri"></param>
        /// <returns></returns>
        public virtual async Task<HtmlDocument> GetHtmlDocumentByPostAsync(string requestUri, HttpContent content)
        {
            try
            {
                var resp = await client.PostAsync(requestUri, content);
                if (resp.IsSuccessStatusCode == false)
                {
                    var eee = await resp.Content.ReadAsStringAsync();
                    return null;
                }

                var html = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(html) == false)
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);
                    return doc;
                }
            }
            catch (Exception ex)
            {
                log?.Error($"{ex.Message}");
            }

            return null;
        }

        public virtual async Task<string> GetDmmPlot(string num)
        {
            const string dmm = "dmm";
            if (string.IsNullOrWhiteSpace(num))
                return null;

            num = num.Replace("-", "").Replace("_", "").ToLower();
            using (await locker.LockAsync(num))
            {
                var item = Plugin.Instance.db.Plots.Find(o => o.num == num && o.provider == dmm).FirstOrDefault();
                if (item != null)
                    return item.plot;

                var url = $"https://www.dmm.co.jp/mono/dvd/-/detail/=/cid={num}/";
                var doc = await GetHtmlDocumentAsync(url);

                if (doc == null)
                    return null;

                var plot = doc.DocumentNode.SelectSingleNode("//tr/td/div[@class='mg-b20 lh4']/p[@class='mg-b20']")?.InnerText?.Trim();
                if (string.IsNullOrWhiteSpace(plot) == false)
                {
                    var dt = DateTime.Now;
                    item = new Data.Plot()
                    {
                        created = dt,
                        modified = dt,
                        num = num,
                        plot = plot,
                        provider = dmm,
                        url = url
                    };
                    Plugin.Instance.db.Plots.Insert(item);
                }

                return plot;
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // 释放托管资源
                    client?.Dispose();
                    requestSemaphore?.Dispose();
                }
                // 释放非托管资源
                disposed = true;
            }
        }
    }
}