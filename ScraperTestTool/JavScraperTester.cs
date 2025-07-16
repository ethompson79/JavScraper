using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using System.Linq;
using System.Text.Json;

/// <summary>
/// JavScraper 独立刮削测试工具
/// 使用方法: dotnet run 独立刮削测试工具.cs
/// </summary>
public class JavScraperTester
{
    private readonly HttpClient httpClient;
    private readonly string testFolder;
    
    // 刮削器配置选项
    public bool EnableJavBus { get; set; } = true;
    public bool EnableJavDB { get; set; } = true;
    public int MinImageThreshold { get; set; } = 3;  // JavBus图片数量阈值

    private string currentTestFolder = null;

    public JavScraperTester()
    {
        // 注册编码提供程序以支持GBK等编码
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        
        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", 
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        
        testFolder = Path.Combine(Directory.GetCurrentDirectory(), "刮削测试结果");
        Directory.CreateDirectory(testFolder);
    }

    public async Task StartInteractiveTest()
    {
        Console.WriteLine("🎬 JavScraper 独立刮削测试工具");
        Console.WriteLine(new string('=', 50));
        Console.WriteLine("输入影片番号进行测试，输入 'exit' 退出");
        Console.WriteLine();

        while (true)
        {
            Console.Write("请输入番号 (如 PRED-066): ");
            string input = Console.ReadLine()?.Trim();
            
            if (string.IsNullOrEmpty(input))
                continue;
                
            if (input.ToLower() == "exit")
                break;
                
            Console.WriteLine();
            await TestMovieScraping(input);
            Console.WriteLine();
            Console.WriteLine(new string('=', 50));
        }
    }

    private async Task TestMovieScraping(string movieId)
    {
        // 重置测试文件夹，确保每次测试都使用新文件夹
        currentTestFolder = null;
        
        Console.WriteLine($"\n🔍 开始刮削: {movieId}");
        
        // 显示当前配置
        Console.WriteLine($"📋 刮削器配置: JavBus={EnableJavBus}, JavDB={EnableJavDB}");
        
        var movie = new MovieInfo { Id = movieId };
        bool javBusSuccess = false;
        bool javDBSuccess = false;

        // 第一阶段：JavBus刮削 (如果启用)
        if (EnableJavBus)
        {
            Console.WriteLine($"\n📡 测试 JavBus...");
            var javBusResult = await ScrapeJavBus(movieId);
            if (javBusResult != null)
            {
                Console.WriteLine($"✅ JavBus 刮削成功");
                await SaveMovieInfo(javBusResult, await CreateTestFolder(movieId), "JavBus");
                MergeMovieInfo(movie, javBusResult);
                javBusSuccess = true;
            }
            else
            {
                Console.WriteLine($"❌ JavBus 刮削失败");
            }
        }

        // 第二阶段：JavDB刮削 (如果启用且需要补全)
        if (EnableJavDB)
        {
            Console.WriteLine($"\n📡 测试 JavDB...");
            
            // 智能决策：如果JavBus已有足够图片，JavDB跳过图片刮削
            bool skipJavDBImages = javBusSuccess && movie.Images.Count >= MinImageThreshold;
            if (skipJavDBImages)
            {
                Console.WriteLine($"   💡 JavBus已有{movie.Images.Count}张图片，JavDB将跳过图片刮削");
            }
            
            var javDBResult = await ScrapeJavDB(movieId, skipImages: skipJavDBImages);
            if (javDBResult != null)
            {
                Console.WriteLine($"✅ JavDB 刮削成功");
                await SaveMovieInfo(javDBResult, await CreateTestFolder(movieId), "JavDB");
                
                // 智能合并：只补全缺失的内容
                MergeMovieInfoSmart(movie, javDBResult, javBusSuccess);
                javDBSuccess = true;
            }
            else
            {
                Console.WriteLine($"❌ JavDB 刮削失败");
            }
        }

        // 结果展示和保存
        if (javBusSuccess || javDBSuccess)
        {
            Console.WriteLine($"\n🎯 合并后的最终结果:");
            DisplayMovieInfo(movie);
            
            string finalFolder = await CreateTestFolder(movieId);
            await SaveMovieInfo(movie, finalFolder, "Combined");
            await DownloadImages(movie, finalFolder);
            
            Console.WriteLine($"\n📁 所有文件已保存到: {finalFolder}");
        }
        else
        {
            Console.WriteLine($"\n❌ 所有刮削器都失败了");
        }
    }

    private async Task<MovieInfo> ScrapeJavBus(string movieId)
    {
        try
        {
            // 智能番号格式修正 - 添加JavDB级别的宽容度
            string normalizedMovieId = NormalizeMovieId(movieId);
            
            string url = $"https://www.javbus.com/{normalizedMovieId}";
            Console.WriteLine($"   访问: {url}");

            // 配置完整的浏览器模拟头部
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", 
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            httpClient.DefaultRequestHeaders.Add("Accept", 
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            httpClient.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8,ja;q=0.7");
            // 移除Accept-Encoding，让HttpClient自动处理压缩
            // httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            httpClient.DefaultRequestHeaders.Add("DNT", "1");
            httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
            httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
            httpClient.DefaultRequestHeaders.Add("Cache-Control", "max-age=0");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            // 步骤1：首次访问，获取年龄验证页面
            var response = await httpClient.GetAsync(url);
            Console.WriteLine($"   初次访问状态码: {response.StatusCode}");
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"   HTTP错误: {response.StatusCode}");
                return null;
            }

            string html = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"   初次页面长度: {html.Length} 字符");
            Console.WriteLine($"   📍 检查点1: 已获取HTML内容");

            // 步骤2：检测并处理年龄验证
            if (html.Contains("Age Verification") || html.Contains("年龄验证") || 
                html.Contains("age_verification") || html.Contains("18+") ||
                html.Length < 5000) // 年龄验证页面通常很短
            {
                Console.WriteLine($"   🔓 检测到年龄验证，开始绕过...");
                
                // 方法1：直接访问年龄验证确认接口
                var verifyUrls = new[] 
                {
                    "https://www.javbus.com/age_verification/confirm",
                    "https://www.javbus.com/verify",
                    $"https://www.javbus.com/{movieId}?confirm=1",
                    $"https://www.javbus.com/{movieId}?age_verified=true"
                };

                foreach (var verifyUrl in verifyUrls)
                {
                    try
                    {
                        Console.WriteLine($"   尝试验证接口: {verifyUrl}");
                        var verifyResponse = await httpClient.GetAsync(verifyUrl);
                        if (verifyResponse.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"   验证接口响应: {verifyResponse.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   验证接口失败: {ex.Message}");
                    }
                }

                // 方法2：模拟POST提交年龄验证
                try
                {
                    var postData = new Dictionary<string, string>
                    {
                        {"age_verified", "true"},
                        {"confirm", "1"},
                        {"over18", "1"},
                        {"redirect", url}
                    };

                    var formContent = new FormUrlEncodedContent(postData);
                    var postResponse = await httpClient.PostAsync("https://www.javbus.com/age_verification", formContent);
                    Console.WriteLine($"   POST验证响应: {postResponse.StatusCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   POST验证失败: {ex.Message}");
                }

                // 方法3：重新访问目标页面
                Console.WriteLine($"   🔄 重新访问目标页面...");
                
                // 添加年龄验证相关的请求头
                httpClient.DefaultRequestHeaders.Remove("Referer");
                httpClient.DefaultRequestHeaders.Add("Referer", "https://www.javbus.com/");
                
                response = await httpClient.GetAsync(url);
                html = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"   重新访问后页面长度: {html.Length} 字符");
            }
            else
            {
                Console.WriteLine($"   📍 检查点2: 未检测到年龄验证，页面长度 {html.Length}");
            }

            // 检查是否成功绕过年龄验证
            if (html.Contains("Age Verification") || html.Contains("年龄验证") || html.Length < 5000)
            {
                Console.WriteLine($"   ❌ 年龄验证绕过失败，页面长度: {html.Length}");
                
                // 显示页面片段用于调试
                var preview = html.Length > 500 ? html.Substring(0, 500) : html;
                Console.WriteLine($"   页面预览: {preview.Replace("\n", " ").Replace("\r", "")}");
                return null;
            }

            Console.WriteLine($"   📍 检查点3: 准备解析HTML");
            Console.WriteLine($"   ✅ 页面访问成功，开始解析内容...");

            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            Console.WriteLine($"   📍 检查点4: HTML文档已加载");

            // 调试：输出页面HTML结构信息
            Console.WriteLine($"   🔍 页面结构分析：");
            
            // 首先显示页面的原始HTML片段
            var htmlPreview = html.Length > 1000 ? html.Substring(0, 1000) : html;
            Console.WriteLine($"   📄 HTML预览前1000字符:");
            Console.WriteLine($"   {htmlPreview.Replace("\n", "\\n").Replace("\r", "\\r")}");
            Console.WriteLine($"   --- HTML预览结束 ---");
            
            // 查看页面标题
            var pageTitle = doc.DocumentNode.SelectSingleNode("//title");
            if (pageTitle != null)
            {
                Console.WriteLine($"   📋 页面标题: '{pageTitle.InnerText?.Trim()}'");
            }
            else
            {
                Console.WriteLine($"   ❌ 未找到title标签");
            }
            
            // 查看所有h1-h6标签
            bool foundHeaders = false;
            for (int i = 1; i <= 6; i++)
            {
                var headers = doc.DocumentNode.SelectNodes($"//h{i}");
                if (headers != null && headers.Count > 0)
                {
                    foundHeaders = true;
                    Console.WriteLine($"   📝 找到 {headers.Count} 个 h{i} 元素:");
                    foreach (var header in headers.Take(3))
                    {
                        var text = header.InnerText?.Trim();
                        if (!string.IsNullOrEmpty(text))
                        {
                            Console.WriteLine($"     h{i}: '{(text.Length > 80 ? text.Substring(0, 80) + "..." : text)}'");
                        }
                    }
                }
            }
            if (!foundHeaders)
            {
                Console.WriteLine($"   ❌ 未找到任何h1-h6标签");
            }
            
            // 查看class包含genre的元素
            var genreElements = doc.DocumentNode.SelectNodes("//*[contains(@class, 'genre')]");
            if (genreElements != null)
            {
                Console.WriteLine($"   找到 {genreElements.Count} 个包含genre类的元素");
                foreach (var element in genreElements.Take(3))
                {
                    Console.WriteLine($"     {element.Name}[@class='{element.GetAttributeValue("class", "")}']");
                }
            }
            
            // 查看class包含star的元素
            var starElements = doc.DocumentNode.SelectNodes("//*[contains(@class, 'star')]");
            if (starElements != null)
            {
                Console.WriteLine($"   找到 {starElements.Count} 个包含star类的元素");
                foreach (var element in starElements.Take(3))
                {
                    Console.WriteLine($"     {element.Name}[@class='{element.GetAttributeValue("class", "")}']");
                }
            }
            
            // 查看所有img标签的src
            var allImages = doc.DocumentNode.SelectNodes("//img");
            if (allImages != null)
            {
                Console.WriteLine($"   找到 {allImages.Count} 个img元素");
                foreach (var img in allImages.Take(5))
                {
                    var src = img.GetAttributeValue("src", "");
                    var className = img.GetAttributeValue("class", "");
                    Console.WriteLine($"     img[class='{className}', src='{(src.Length > 50 ? src.Substring(0, 50) + "..." : src)}']");
                }
            }

            var movie = new MovieInfo { Id = movieId };

            // 增强的标题提取 - 使用多种选择器
            var titleSelectors = new[] 
            {
                "//h3[contains(@class, 'title')]",
                "//h3",
                "//div[contains(@class, 'info')]//h3",
                "//div[@class='container']//h3",
                "//title"
            };

            foreach (var selector in titleSelectors)
            {
                var titleNode = doc.DocumentNode.SelectSingleNode(selector);
                if (titleNode != null && !string.IsNullOrEmpty(titleNode.InnerText?.Trim()))
                {
                    var title = CleanTitle(titleNode.InnerText);
                    if (!string.IsNullOrEmpty(title) && !title.ToLower().Contains("javbus") && 
                        !title.ToLower().Contains("age verification"))
                    {
                        movie.Title = title;
                        Console.WriteLine($"   📝 标题: {movie.Title}");
                        break;
                    }
                }
            }

            // 增强的演员提取
            var actorSelectors = new[]
            {
                "//div[@class='star-name']//a",
                "//span[@class='genre']//a[contains(@href, '/star/')]",
                "//div[contains(@class, 'star')]//a",
                "//a[contains(@href, '/star/')]"
            };

            foreach (var selector in actorSelectors)
            {
                var actorNodes = doc.DocumentNode.SelectNodes(selector);
                if (actorNodes != null)
                {
                    foreach (var node in actorNodes.Take(5))
                    {
                        var actorName = CleanTitle(node.InnerText);
                        if (!string.IsNullOrEmpty(actorName) && !movie.Actors.Contains(actorName))
                        {
                            movie.Actors.Add(actorName);
                        }
                    }
                    if (movie.Actors.Any()) break;
                }
            }
            Console.WriteLine($"   👥 演员: {string.Join(", ", movie.Actors)}");

            // 增强的标签提取
            var tagSelectors = new[]
            {
                "//span[@class='genre']//a[not(contains(@href, '/star/'))]",
                "//div[@class='genre']//a",
                "//span[contains(@class, 'genre')]//a",
                "//a[contains(@href, '/genre/')]"
            };

            foreach (var selector in tagSelectors)
            {
                var tagNodes = doc.DocumentNode.SelectNodes(selector);
                if (tagNodes != null)
                {
                    foreach (var node in tagNodes.Take(10))
                    {
                        var tag = CleanTitle(node.InnerText);
                        if (!string.IsNullOrEmpty(tag) && !movie.Tags.Contains(tag))
                        {
                            movie.Tags.Add(tag);
                        }
                    }
                    if (movie.Tags.Any()) break;
                }
            }
            Console.WriteLine($"   🏷️ 标签: {string.Join(", ", movie.Tags)}");

            // 增强的图片提取 - 优先获取高质量大图
            Console.WriteLine($"   🔍 开始搜索图片...");
            
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
                        else if (href.StartsWith("/")) href = "https://www.javbus.com" + href;
                        
                        if (href.StartsWith("http") && !movie.Images.Contains(href))
                        {
                            movie.Images.Add(href);
                            Console.WriteLine($"   📸 找到高清封面: {Path.GetFileName(href)}");
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
                        else if (href.StartsWith("/")) href = "https://www.javbus.com" + href;
                        
                        if (href.StartsWith("http") && !movie.Images.Contains(href))
                        {
                            movie.Images.Add(href);
                            Console.WriteLine($"   🖼️ 找到样品图片: {Path.GetFileName(href)}");
                        }
                    }
                }
            }
            
            // 第三优先级：如果上面都没找到，尝试直接的img标签 (作为备用)
            if (movie.Images.Count == 0)
            {
                Console.WriteLine($"   ⚠️ 未找到链接图片，尝试直接img标签...");
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
                                else if (src.StartsWith("/")) src = "https://www.javbus.com" + src;
                                
                                if (src.StartsWith("http") && !movie.Images.Contains(src))
                                {
                                    movie.Images.Add(src);
                                    Console.WriteLine($"   🔗 备用图片: {Path.GetFileName(src)}");
                                }
                            }
                        }
                        if (movie.Images.Any()) break;
                    }
                }
            }
            
            Console.WriteLine($"   🖼️ 图片获取完成: {movie.Images.Count} 张");

            if (string.IsNullOrEmpty(movie.Title) && !movie.Actors.Any() && !movie.Tags.Any())
            {
                Console.WriteLine($"   ❌ 未找到有效内容，可能页面结构不匹配");
                
                // 调试信息：显示找到的关键元素
                var h3Elements = doc.DocumentNode.SelectNodes("//h3");
                if (h3Elements != null)
                {
                    Console.WriteLine($"   调试: 找到 {h3Elements.Count} 个 h3 元素");
                    foreach (var h3 in h3Elements.Take(3))
                    {
                        Console.WriteLine($"   H3: {h3.InnerText?.Trim()}");
                    }
                }
                
                return null;
            }

            Console.WriteLine($"   ✅ JavBus刮削成功!");
            return movie;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ JavBus刮削异常: {ex.Message}");
            return null;
        }
    }

    private async Task<MovieInfo> ScrapeJavDB(string movieId, bool skipImages = false)
    {
        try
        {
            string url = $"https://javdb.com/search?q={movieId}&f=all";
            Console.WriteLine($"   访问: {url}");

            var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"   HTTP错误: {response.StatusCode}");
                return null;
            }

            var html = await response.Content.ReadAsStringAsync();
            
            // 调试信息：检查搜索页面
            Console.WriteLine($"   搜索页面长度: {html.Length} 字符");
            if (html.Length < 1000)
            {
                Console.WriteLine($"   警告: 搜索页面内容过短");
                Console.WriteLine($"   HTML预览: {html.Substring(0, Math.Min(500, html.Length))}");
                Console.WriteLine("   ---");
            }
            
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // 查找第一个搜索结果 - 多种选择器
            var movieLink = doc.DocumentNode.SelectSingleNode("//a[contains(@class, 'box')]/@href") ??
                           doc.DocumentNode.SelectSingleNode("//div[@class='item']//a/@href") ??
                           doc.DocumentNode.SelectSingleNode("//div[contains(@class,'grid-item')]//a/@href");
            if (movieLink == null)
            {
                Console.WriteLine("   未找到搜索结果");
                // 显示一些页面内容用于调试
                var pageTitle = doc.DocumentNode.SelectSingleNode("//title")?.InnerText;
                Console.WriteLine($"   页面标题: {pageTitle}");
                return null;
            }

            string detailUrl = "https://javdb.com" + movieLink.GetAttributeValue("href", "");
            Console.WriteLine($"   详情页: {detailUrl}");

            // 访问详情页
            var detailResponse = await httpClient.GetAsync(detailUrl);
            var detailHtml = await detailResponse.Content.ReadAsStringAsync();
            var detailDoc = new HtmlDocument();
            detailDoc.LoadHtml(detailHtml);

            var movie = new MovieInfo { Id = movieId };

            // 提取标题
            var titleNode = detailDoc.DocumentNode.SelectSingleNode("//h2[@class='title is-4']") ??
                           detailDoc.DocumentNode.SelectSingleNode("//title");
            if (titleNode != null)
            {
                movie.Title = CleanTitle(titleNode.InnerText);
                Console.WriteLine($"   标题: {movie.Title}");
            }

            // 提取演员
            var actorNodes = detailDoc.DocumentNode.SelectNodes("//strong[text()='演員:']/following-sibling::span//a") ??
                            detailDoc.DocumentNode.SelectNodes("//span[contains(@class, 'actor')]//a");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    string actorName = actor.InnerText?.Trim();
                    if (!string.IsNullOrEmpty(actorName))
                    {
                        movie.Actors.Add(actorName);
                    }
                }
                Console.WriteLine($"   演员: {string.Join(", ", movie.Actors)}");
            }

            // 提取封面 (如果不跳过图片)
            if (!skipImages)
            {
                var coverNode = detailDoc.DocumentNode.SelectSingleNode("//img[@class='video-cover']") ??
                               detailDoc.DocumentNode.SelectSingleNode("//img[contains(@src, 'cover')]");
                if (coverNode != null)
                {
                    string coverUrl = coverNode.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(coverUrl))
                    {
                        if (!coverUrl.StartsWith("http"))
                            coverUrl = "https://javdb.com" + coverUrl;
                        movie.Images.Add(coverUrl);
                        Console.WriteLine($"   封面: {coverUrl}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"   🚫 跳过图片刮削 (JavBus已有足够图片)");
            }

            // 提取标签
            var tagNodes = detailDoc.DocumentNode.SelectNodes("//strong[text()='類別:']/following-sibling::span//a") ??
                          detailDoc.DocumentNode.SelectNodes("//span[contains(@class, 'tag')]//a");
            if (tagNodes != null)
            {
                foreach (var tag in tagNodes)
                {
                    string tagName = tag.InnerText?.Trim();
                    if (!string.IsNullOrEmpty(tagName))
                    {
                        movie.Tags.Add(tagName);
                    }
                }
                Console.WriteLine($"   标签: {string.Join(", ", movie.Tags.Take(5))}...");
            }

            return movie;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   错误: {ex.Message}");
            return null;
        }
    }

    private string NormalizeMovieId(string movieId)
    {
        if (string.IsNullOrEmpty(movieId))
            return movieId;

        // 移除空格和特殊字符
        movieId = movieId.Trim().ToUpper();

        // 如果已经包含连字符，直接返回
        if (movieId.Contains("-"))
            return movieId;

        // 智能添加连字符：匹配常见的番号格式
        // 格式1: ABC123 -> ABC-123 (字母+数字)
        var match1 = System.Text.RegularExpressions.Regex.Match(movieId, @"^([A-Z]+)(\d+)$");
        if (match1.Success)
        {
            var normalized = $"{match1.Groups[1].Value}-{match1.Groups[2].Value}";
            Console.WriteLine($"   📝 番号格式标准化: {movieId} -> {normalized}");
            return normalized;
        }

        // 格式2: ABC123D -> ABC-123D (字母+数字+字母)
        var match2 = System.Text.RegularExpressions.Regex.Match(movieId, @"^([A-Z]+)(\d+[A-Z]*)$");
        if (match2.Success)
        {
            var normalized = $"{match2.Groups[1].Value}-{match2.Groups[2].Value}";
            Console.WriteLine($"   📝 番号格式标准化: {movieId} -> {normalized}");
            return normalized;
        }

        // 如果不匹配已知格式，返回原始值
        Console.WriteLine($"   📝 番号格式保持原样: {movieId}");
        return movieId;
    }

    private string CleanTitle(string title)
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

    private void MergeMovieInfo(MovieInfo target, MovieInfo source)
    {
        if (string.IsNullOrEmpty(target.Title) && !string.IsNullOrEmpty(source.Title))
            target.Title = source.Title;
        
        if (string.IsNullOrEmpty(target.Plot) && !string.IsNullOrEmpty(source.Plot))
            target.Plot = source.Plot;

        foreach (var actor in source.Actors)
        {
            if (!target.Actors.Contains(actor))
                target.Actors.Add(actor);
        }

        foreach (var tag in source.Tags)
        {
            if (!target.Tags.Contains(tag))
                target.Tags.Add(tag);
        }

        foreach (var image in source.Images)
        {
            if (!target.Images.Contains(image))
                target.Images.Add(image);
        }
    }

    private void MergeMovieInfoSmart(MovieInfo target, MovieInfo source, bool javBusSuccess)
    {
        // 智能合并标题：JavBus优先，JavDB补全
        if (string.IsNullOrEmpty(target.Title) && !string.IsNullOrEmpty(source.Title))
            target.Title = source.Title;
        
        // 智能合并剧情：JavBus优先，JavDB补全
        if (string.IsNullOrEmpty(target.Plot) && !string.IsNullOrEmpty(source.Plot))
            target.Plot = source.Plot;

        // 合并演员信息（去重）
        foreach (var actor in source.Actors)
        {
            if (!target.Actors.Contains(actor))
                target.Actors.Add(actor);
        }

        // 合并标签信息（去重）
        foreach (var tag in source.Tags)
        {
            if (!target.Tags.Contains(tag))
                target.Tags.Add(tag);
        }

        // 智能合并图片：如果JavBus成功且图片足够，只添加不重复的图片
        foreach (var image in source.Images)
        {
            if (!target.Images.Contains(image))
                target.Images.Add(image);
        }
    }

    private void DisplayMovieInfo(MovieInfo movie)
    {
        Console.WriteLine($"📽️ 番号: {movie.Id}");
        Console.WriteLine($"📝 标题: {movie.Title}");
        Console.WriteLine($"👥 演员: {string.Join(", ", movie.Actors)}");
        Console.WriteLine($"🏷️ 标签: {string.Join(", ", movie.Tags.Take(10))}");
        Console.WriteLine($"🖼️ 图片数: {movie.Images.Count}");
        
        if (!string.IsNullOrEmpty(movie.Plot))
        {
            Console.WriteLine($"📄 简介: {movie.Plot.Substring(0, Math.Min(100, movie.Plot.Length))}...");
        }
    }

    private async Task SaveMovieInfo(MovieInfo movie, string folder, string source)
    {
        string fileName = Path.Combine(folder, $"{source}_info.json");
        string json = JsonSerializer.Serialize(movie, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
        });
        await File.WriteAllTextAsync(fileName, json, Encoding.UTF8);
        Console.WriteLine($"   💾 信息已保存: {fileName}");
    }

    private async Task DownloadImages(MovieInfo movie, string folder)
    {
        if (movie.Images.Count == 0)
        {
            Console.WriteLine("📷 没有图片需要下载");
            return;
        }

        Console.WriteLine($"\n📷 开始下载 {movie.Images.Count} 张图片...");
        
        // 创建extrafanart子文件夹用于存放其他图片
        string extrafanartFolder = Path.Combine(folder, "extrafanart");
        Directory.CreateDirectory(extrafanartFolder);

        int downloaded = 0;
        int extraImageCounter = 1;
        bool hasCover = false;      // 是否已有封面
        bool hasPoster = false;     // 是否已有海报
        
        for (int i = 0; i < movie.Images.Count; i++)
        {
            try
            {
                string imageUrl = movie.Images[i];
                string fileName = Path.GetFileName(imageUrl).Split('?')[0];
                Console.WriteLine($"   下载 ({i + 1}/{movie.Images.Count}): {fileName}");
                
                // 智能防盗链处理
                httpClient.DefaultRequestHeaders.Remove("Referer");
                if (imageUrl.Contains("javbus") || imageUrl.Contains("buscdn") || imageUrl.Contains("javbus22"))
                {
                    httpClient.DefaultRequestHeaders.Add("Referer", "https://www.javbus.com/");
                    Console.WriteLine($"   🔗 设置JavBus Referer");
                }
                else if (imageUrl.Contains("javdb") || imageUrl.Contains("jdbstatic"))
                {
                    httpClient.DefaultRequestHeaders.Add("Referer", "https://javdb.com/");
                    Console.WriteLine($"   🔗 设置JavDB Referer");
                }
                else
                {
                    httpClient.DefaultRequestHeaders.Add("Referer", "https://www.google.com/");
                    Console.WriteLine($"   🔗 设置通用Referer");
                }
                
                var response = await httpClient.GetAsync(imageUrl);
                if (response.IsSuccessStatusCode)
                {
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    string extension = Path.GetExtension(fileName);
                    if (string.IsNullOrEmpty(extension)) extension = ".jpg";
                    
                    // 检测图片尺寸
                    var (width, height) = GetImageDimensions(imageBytes);
                    bool isPortrait = height > width; // 竖版图片
                    
                    Console.WriteLine($"   📐 图片尺寸: {width}x{height} {(isPortrait ? "(竖版)" : "(横版)")}");
                    
                    // 智能命名规则
                    string finalFileName;
                    string savePath;
                    
                    if (!hasPoster && isPortrait)
                    {
                        // 第一张竖版图片作为海报 (poster)
                        finalFileName = $"{movie.Id}-poster{extension}";
                        savePath = Path.Combine(folder, finalFileName);
                        Console.WriteLine($"   🎭 保存为海报: {finalFileName}");
                        hasPoster = true;
                    }
                    else if (!hasCover && !isPortrait)
                    {
                        // 第一张横版图片作为封面 (fanart)
                        finalFileName = $"{movie.Id}-fanart{extension}";
                        savePath = Path.Combine(folder, finalFileName);
                        Console.WriteLine($"   🖼️ 保存为封面: {finalFileName}");
                        hasCover = true;
                    }
                    else
                    {
                        // 其他图片都放在extrafanart文件夹，按数字编号
                        finalFileName = $"{movie.Id}-{extraImageCounter}{extension}";
                        savePath = Path.Combine(extrafanartFolder, finalFileName);
                        Console.WriteLine($"   📸 保存为额外图片: extrafanart/{finalFileName}");
                        extraImageCounter++;
                    }
                    
                    await File.WriteAllBytesAsync(savePath, imageBytes);
                    downloaded++;
                    Console.WriteLine($"   ✅ 已保存: {savePath} ({imageBytes.Length} bytes)");
                }
                else
                {
                    Console.WriteLine($"   ❌ 下载失败: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ 下载错误: {ex.Message}");
            }
        }
        
        Console.WriteLine($"📷 图片下载完成: {downloaded}/{movie.Images.Count} 成功");
        if (downloaded > 0)
        {
            Console.WriteLine($"   📁 主文件夹: {(hasCover ? "封面" : "")}{(hasCover && hasPoster ? "、" : "")}{(hasPoster ? "海报" : "")}");
            if (extraImageCounter > 1)
            {
                Console.WriteLine($"   📁 extrafanart/ 文件夹: {extraImageCounter - 1} 张额外图片");
            }
        }
    }

    // 添加图片尺寸检测方法
    private (int width, int height) GetImageDimensions(byte[] imageBytes)
    {
        try
        {
            using (var stream = new MemoryStream(imageBytes))
            {
                // 简单的JPEG尺寸检测
                if (imageBytes.Length > 10 && imageBytes[0] == 0xFF && imageBytes[1] == 0xD8)
                {
                    return GetJpegDimensions(imageBytes);
                }
                // 简单的PNG尺寸检测
                else if (imageBytes.Length > 24 && 
                         imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && 
                         imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
                {
                    return GetPngDimensions(imageBytes);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️ 图片尺寸检测失败: {ex.Message}");
        }
        
        // 默认假设为横版
        return (1920, 1080);
    }
    
    private (int width, int height) GetJpegDimensions(byte[] imageBytes)
    {
        try
        {
            for (int i = 2; i < imageBytes.Length - 8; i++)
            {
                if (imageBytes[i] == 0xFF && (imageBytes[i + 1] == 0xC0 || imageBytes[i + 1] == 0xC2))
                {
                    int height = (imageBytes[i + 5] << 8) | imageBytes[i + 6];
                    int width = (imageBytes[i + 7] << 8) | imageBytes[i + 8];
                    return (width, height);
                }
            }
        }
        catch { }
        return (1920, 1080); // 默认值
    }
    
    private (int width, int height) GetPngDimensions(byte[] imageBytes)
    {
        try
        {
            int width = (imageBytes[16] << 24) | (imageBytes[17] << 16) | (imageBytes[18] << 8) | imageBytes[19];
            int height = (imageBytes[20] << 24) | (imageBytes[21] << 16) | (imageBytes[22] << 8) | imageBytes[23];
            return (width, height);
        }
        catch { }
        return (1920, 1080); // 默认值
    }

    private async Task<string> CreateTestFolder(string movieId)
    {
        if (currentTestFolder == null)
        {
            currentTestFolder = Path.Combine(testFolder, $"{movieId}_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(currentTestFolder);
            Console.WriteLine($"\n📁 创建测试文件夹: {Path.GetFileName(currentTestFolder)}");
        }
        return currentTestFolder;
    }

    public static async Task Main(string[] args)
    {
        try
        {
            var tester = new JavScraperTester();
            
            if (args.Length > 0)
            {
                // 命令行模式
                foreach (string movieId in args)
                {
                    await tester.TestMovieScraping(movieId);
                    Console.WriteLine();
                }
            }
            else
            {
                // 交互模式
                await tester.StartInteractiveTest();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 程序错误: {ex.Message}");
        }
        
        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();
    }
}

public class MovieInfo
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Plot { get; set; } = "";
    public List<string> Actors { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public List<string> Images { get; set; } = new();
    public DateTime ScrapedAt { get; set; } = DateTime.Now;
} 