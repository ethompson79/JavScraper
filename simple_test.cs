using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

class Program
{
    private static readonly HttpClient client = new HttpClient();
    
    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("用法: dotnet run simple_test.cs <番号>");
            Console.WriteLine("例如: dotnet run simple_test.cs PRED-066");
            return;
        }
        
        string movieId = args[0];
        Console.WriteLine($"🎯 开始测试刮削功能 - 番号: {movieId}");
        Console.WriteLine("=" * 50);
        
        // 设置User-Agent
        client.DefaultRequestHeaders.Add("User-Agent", 
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        
        await TestJavDB(movieId);
        await TestJavBus(movieId);
    }
    
    static async Task TestJavDB(string movieId)
    {
        Console.WriteLine("\n🔍 测试 JavDB 刮削...");
        try
        {
            // 搜索页面
            string searchUrl = $"https://javdb.com/search?q={movieId}&f=all";
            Console.WriteLine($"搜索URL: {searchUrl}");
            
            var response = await client.GetAsync(searchUrl);
            if (response.IsSuccessStatusCode)
            {
                string html = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"✅ JavDB 搜索成功，HTML长度: {html.Length}");
                
                // 简单检查是否找到结果
                if (html.Contains(movieId))
                {
                    Console.WriteLine($"✅ 找到番号 {movieId} 相关内容");
                }
                else
                {
                    Console.WriteLine($"❌ 未找到番号 {movieId} 相关内容");
                }
            }
            else
            {
                Console.WriteLine($"❌ JavDB 搜索失败: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ JavDB 测试异常: {ex.Message}");
        }
    }
    
    static async Task TestJavBus(string movieId)
    {
        Console.WriteLine("\n🔍 测试 JavBus 刮削...");
        try
        {
            // 搜索页面
            string searchUrl = $"https://www.javbus.com/search/{movieId}";
            Console.WriteLine($"搜索URL: {searchUrl}");
            
            var response = await client.GetAsync(searchUrl);
            if (response.IsSuccessStatusCode)
            {
                string html = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"✅ JavBus 搜索成功，HTML长度: {html.Length}");
                
                // 简单检查是否找到结果
                if (html.Contains(movieId))
                {
                    Console.WriteLine($"✅ 找到番号 {movieId} 相关内容");
                }
                else
                {
                    Console.WriteLine($"❌ 未找到番号 {movieId} 相关内容");
                }
            }
            else
            {
                Console.WriteLine($"❌ JavBus 搜索失败: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ JavBus 测试异常: {ex.Message}");
        }
    }
}
