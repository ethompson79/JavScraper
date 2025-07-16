using System;
using System.IO;
using System.Reflection;

class Program
{
    static void Main()
    {
        string dllPath = "JavScraper-编号004-彻底修复版-v1.2025.712.13.dll";
        
        if (!File.Exists(dllPath))
        {
            Console.WriteLine("DLL文件不存在！");
            return;
        }
        
        try
        {
            var assembly = Assembly.LoadFrom(Path.GetFullPath(dllPath));
            
            // 检查嵌入的HTML资源
            var resources = assembly.GetManifestResourceNames();
            string configPageResource = null;
            
            foreach (var resource in resources)
            {
                if (resource.Contains("ConfigPage.html"))
                {
                    configPageResource = resource;
                    break;
                }
            }
            
            if (configPageResource == null)
            {
                Console.WriteLine("❌ 没有找到ConfigPage.html资源！");
                return;
            }
            
            // 读取HTML内容
            using (var stream = assembly.GetManifestResourceStream(configPageResource))
            using (var reader = new StreamReader(stream))
            {
                string htmlContent = reader.ReadToEnd();
                
                Console.WriteLine("=== 检查代理选项 ===");
                
                // 检查是否还有HTTP/HTTPS/Socks5选项
                if (htmlContent.Contains("HTTP") && htmlContent.Contains("option"))
                {
                    Console.WriteLine("❌ 仍然包含HTTP选项！");
                }
                else
                {
                    Console.WriteLine("✅ 没有HTTP选项");
                }
                
                if (htmlContent.Contains("HTTPS") && htmlContent.Contains("option"))
                {
                    Console.WriteLine("❌ 仍然包含HTTPS选项！");
                }
                else
                {
                    Console.WriteLine("✅ 没有HTTPS选项");
                }
                
                if (htmlContent.Contains("Socks5") && htmlContent.Contains("option"))
                {
                    Console.WriteLine("❌ 仍然包含Socks5选项！");
                }
                else
                {
                    Console.WriteLine("✅ 没有Socks5选项");
                }
                
                // 检查JsProxy选项
                if (htmlContent.Contains("JsProxy (CloudFlare Workers)"))
                {
                    Console.WriteLine("✅ 包含JsProxy (CloudFlare Workers)选项");
                }
                else
                {
                    Console.WriteLine("❌ 没有JsProxy (CloudFlare Workers)选项！");
                }
                
                Console.WriteLine("\n=== 检查刮削器名称 ===");
                
                // 检查刮削器名称
                if (htmlContent.Contains("JavBus"))
                {
                    Console.WriteLine("✅ 包含JavBus");
                }
                else
                {
                    Console.WriteLine("❌ 没有JavBus！");
                }
                
                if (htmlContent.Contains("JavDB"))
                {
                    Console.WriteLine("✅ 包含JavDB");
                }
                else
                {
                    Console.WriteLine("❌ 没有JavDB！");
                }
                
                if (htmlContent.Contains("AAAA") || htmlContent.Contains("BBBB") || htmlContent.Contains("CCCC"))
                {
                    Console.WriteLine("❌ 仍然包含AAAA/BBBB/CCCC！");
                }
                else
                {
                    Console.WriteLine("✅ 没有AAAA/BBBB/CCCC");
                }
                
                // 输出代理选择器部分
                Console.WriteLine("\n=== 代理选择器HTML ===");
                int selectStart = htmlContent.IndexOf("selectProxyType");
                if (selectStart > 0)
                {
                    int start = htmlContent.LastIndexOf("<select", selectStart);
                    int end = htmlContent.IndexOf("</select>", selectStart) + 9;
                    if (start > 0 && end > start)
                    {
                        string selectHtml = htmlContent.Substring(start, end - start);
                        Console.WriteLine(selectHtml);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"错误: {ex.Message}");
        }
    }
}
