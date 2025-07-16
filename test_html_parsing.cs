using System;
using HtmlAgilityPack;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        Console.WriteLine("测试HTML解析逻辑...");
        
        // 模拟JavBus的HTML结构（简化版）
        var sampleHtml = @"
        <!DOCTYPE html>
        <html>
        <head><title>JavBus Search Results</title></head>
        <body>
            <div id='waterfall'>
                <div class='row'>
                    <div class='col-sm-3'>
                        <a class='movie-box' href='/PRED-066'>
                            <div class='photo-frame'>
                                <img src='cover1.jpg' title='PRED-066 部活合宿NTR' alt='PRED-066'>
                            </div>
                            <date>2023-01-15</date>
                        </a>
                    </div>
                    <div class='col-sm-3'>
                        <a class='movie-box' href='/ABP-933'>
                            <div class='photo-frame'>
                                <img src='cover2.jpg' title='ABP-933 Sample Title' alt='ABP-933'>
                            </div>
                            <date>2023-02-20</date>
                        </a>
                    </div>
                </div>
            </div>
        </body>
        </html>";
        
        TestHtmlParsing(sampleHtml);
        
        // 测试编码检测逻辑
        TestEncodingDetection();
        
        Console.WriteLine("\n测试完成。按任意键退出...");
        Console.ReadKey();
    }
    
    static void TestHtmlParsing(string html)
    {
        Console.WriteLine("\n=== 测试HTML解析 ===");
        
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        
        // 使用我们修复的选择器
        var selectors = new[]
        {
            "//a[@class='movie-box']",
            "//div[@class='movie-box']//a",
            "//div[contains(@class,'movie-box')]//a",
            "//div[@id='waterfall']//a[@class='movie-box']",
            "//div[@id='waterfall']//div//a",
            "//div[@id='waterfall']//a[contains(@class,'movie-box')]",
            "//div[@class='item']//a",
            "//div[contains(@class,'item')]//a",
            "//div[@class='video-item']//a",
            "//div[contains(@class,'video-item')]//a",
            "//div[@class='row']//div[@class='col-sm-3']//a",
            "//div[contains(@class,'col-')]//a[contains(@class,'movie-box')]",
            "//a[contains(@href,'/') and img[@src]]",
            "//a[img]"
        };
        
        foreach (var selector in selectors)
        {
            var nodes = doc.DocumentNode.SelectNodes(selector);
            if (nodes?.Any() == true)
            {
                Console.WriteLine($"选择器 '{selector}' 找到 {nodes.Count} 个节点");
                
                foreach (var node in nodes)
                {
                    var href = node.GetAttributeValue("href", "");
                    var img = node.SelectSingleNode(".//img");
                    var title = img?.GetAttributeValue("title", "") ?? img?.GetAttributeValue("alt", "");
                    
                    Console.WriteLine($"  链接: {href}, 标题: {title}");
                    
                    // 测试链接过滤逻辑
                    if (TestLinkFilter(href, node))
                    {
                        Console.WriteLine($"    ✓ 链接通过过滤");
                    }
                    else
                    {
                        Console.WriteLine($"    ✗ 链接被过滤");
                    }
                }
                break; // 找到第一个有效选择器就停止
            }
        }
    }
    
    static bool TestLinkFilter(string href, HtmlNode node)
    {
        // 复制我们修复的链接过滤逻辑
        if (string.IsNullOrEmpty(href) || !href.Contains("/"))
            return false;
            
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
        
        var hasImage = node.SelectSingleNode(".//img") != null;
        
        var looksLikeMovie = 
            Regex.IsMatch(href, @"/[A-Za-z]+[-_]?\d+") ||
            Regex.IsMatch(href, @"/\d+[A-Za-z]+") ||
            Regex.IsMatch(href, @"/[A-Za-z]+\d+") ||
            Regex.IsMatch(href, @"/[a-zA-Z0-9]{5,}$") ||
            (hasImage && href.Length > 10 && !href.EndsWith("/"));
        
        return looksLikeMovie;
    }
    
    static void TestEncodingDetection()
    {
        Console.WriteLine("\n=== 测试编码检测 ===");
        
        // 测试UTF-8
        var utf8Text = "测试UTF-8编码：部活合宿NTR";
        var utf8Bytes = Encoding.UTF8.GetBytes(utf8Text);
        TestEncoding("UTF-8", utf8Bytes, utf8Text);
        
        // 测试GBK
        try
        {
            var gbkEncoding = Encoding.GetEncoding("GBK");
            var gbkBytes = gbkEncoding.GetBytes(utf8Text);
            TestEncoding("GBK", gbkBytes, utf8Text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GBK编码测试失败: {ex.Message}");
        }
        
        // 测试损坏的数据
        var corruptedBytes = new byte[] { 0xFF, 0xFE, 0x00, 0x41, 0x42, 0x43 };
        TestEncoding("损坏数据", corruptedBytes, "");
    }
    
    static void TestEncoding(string encodingName, byte[] bytes, string expected)
    {
        Console.WriteLine($"\n测试 {encodingName} 编码:");
        Console.WriteLine($"  原始字节数: {bytes.Length}");
        
        try
        {
            // 模拟我们的编码检测逻辑
            string html;
            
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                html = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
                Console.WriteLine($"  检测到UTF-8 BOM");
            }
            else
            {
                html = Encoding.UTF8.GetString(bytes);
                
                bool hasEncodingIssues = html.Contains("\uFFFD") || 
                                       html.Contains("") || 
                                       html.Contains("\\x") || 
                                       html.Length < bytes.Length / 3;
                
                if (!hasEncodingIssues && html.Length > 10)
                {
                    var visibleChars = html.Count(c => !char.IsControl(c) && !char.IsWhiteSpace(c));
                    var ratio = (double)visibleChars / html.Length;
                    if (ratio < 0.3)
                        hasEncodingIssues = true;
                }
                
                if (hasEncodingIssues)
                {
                    Console.WriteLine($"  UTF-8解码有问题，尝试其他编码...");
                    try
                    {
                        var gbkEncoding = Encoding.GetEncoding("GBK");
                        var gbkHtml = gbkEncoding.GetString(bytes);
                        if (!gbkHtml.Contains("\uFFFD") && gbkHtml.Length > html.Length * 0.8)
                        {
                            html = gbkHtml;
                            Console.WriteLine($"  使用GBK编码成功");
                        }
                    }
                    catch
                    {
                        Console.WriteLine($"  GBK编码失败，使用默认编码");
                    }
                }
                else
                {
                    Console.WriteLine($"  UTF-8解码看起来正常");
                }
            }
            
            Console.WriteLine($"  解码结果: {html}");
            Console.WriteLine($"  解码长度: {html.Length}");
            
            if (!string.IsNullOrEmpty(expected))
            {
                Console.WriteLine($"  期望结果: {expected}");
                Console.WriteLine($"  匹配: {html.Contains(expected.Substring(0, Math.Min(5, expected.Length)))}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  编码测试出错: {ex.Message}");
        }
    }
} 