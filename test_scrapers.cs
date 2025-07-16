using System;
using System.Threading.Tasks;
using System.Linq;
using Emby.Plugins.JavScraper.Scrapers;
using MediaBrowser.Model.Logging;

namespace ScraperTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("开始测试刮削器...");
            
            // 创建简单的日志
            var logManager = new SimpleLogManager();
            
            // 测试JavBus
            Console.WriteLine("\n=== 测试 JavBus ===");
            var javBus = new JavBus(logManager);
            await TestScraper(javBus, "PRED-066");
            
            // 测试JavDB  
            Console.WriteLine("\n=== 测试 JavDB ===");
            var javDB = new JavDB(logManager);
            await TestScraper(javDB, "PRED-066");
            
            Console.WriteLine("\n测试完成，按任意键退出...");
            Console.ReadKey();
        }
        
        static async Task TestScraper(AbstractScraper scraper, string keyword)
        {
            try
            {
                Console.WriteLine($"测试刮削器: {scraper.Name}");
                Console.WriteLine($"搜索关键字: {keyword}");
                
                // 测试搜索
                var searchResults = await scraper.Query(keyword);
                Console.WriteLine($"搜索结果数量: {searchResults?.Count ?? 0}");
                
                if (searchResults?.Any() == true)
                {
                    var first = searchResults.First();
                    Console.WriteLine($"第一个结果: {first.Num} - {first.Title}");
                    Console.WriteLine($"URL: {first.Url}");
                    Console.WriteLine($"封面: {first.Cover}");
                    
                    // 测试获取详情
                    Console.WriteLine("获取详情中...");
                    var detail = await scraper.Get(first.Url);
                    
                    if (detail != null)
                    {
                        Console.WriteLine($"标题: {detail.Title}");
                        Console.WriteLine($"简介: {detail.Plot?.Substring(0, Math.Min(100, detail.Plot?.Length ?? 0))}...");
                        Console.WriteLine($"演员: {string.Join(", ", detail.Actors?.Take(3) ?? new string[0])}");
                        Console.WriteLine($"类别: {string.Join(", ", detail.Genres?.Take(5) ?? new string[0])}");
                        Console.WriteLine($"制作商: {detail.Studio}");
                        Console.WriteLine($"发行商: {detail.Maker}");
                        Console.WriteLine($"系列: {detail.Set}");
                        Console.WriteLine($"时长: {detail.Runtime}");
                        Console.WriteLine($"样片数量: {detail.Samples?.Count ?? 0}");
                    }
                    else
                    {
                        Console.WriteLine("获取详情失败");
                    }
                }
                else
                {
                    Console.WriteLine("没有找到搜索结果");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"测试出错: {ex.Message}");
                Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
            }
        }
    }
    
    // 简单的日志实现
    public class SimpleLogManager : ILogManager
    {
        public ILogger GetLogger(string name) => new SimpleLogger(name);
        public ILogger CreateLogger<T>() => new SimpleLogger(typeof(T).Name);
        public void ReloadLogger(LogLevel level, Func<string, LogLevel, bool> logFilter) { }
        public void Flush() { }
        public LogLevel LogLevel { get; set; } = LogLevel.Info;
        public string ExceptionMessagePrefix { get; set; } = "";
        public void AddConsoleOutput() { }
        public void RemoveConsoleOutput() { }
        public void AddLoggingFileTarget(string path, string appName, LogLevel logLevel) { }
        public void ReloadLogLevel(LogLevel logLevel) { }
        public void ReloadLogLevel(LogLevel logLevel, Func<string, LogLevel, bool> logFilter) { }
    }
    
    public class SimpleLogger : ILogger
    {
        private readonly string _name;
        
        public SimpleLogger(string name)
        {
            _name = name;
        }
        
        public void Info(string message, params object[] paramList)
        {
            Console.WriteLine($"[INFO] {_name}: {message}");
        }
        
        public void Error(string message, params object[] paramList)
        {
            Console.WriteLine($"[ERROR] {_name}: {message}");
        }
        
        public void Warn(string message, params object[] paramList)
        {
            Console.WriteLine($"[WARN] {_name}: {message}");
        }
        
        public void Debug(string message, params object[] paramList)
        {
            Console.WriteLine($"[DEBUG] {_name}: {message}");
        }
        
        public void Fatal(string message, params object[] paramList)
        {
            Console.WriteLine($"[FATAL] {_name}: {message}");
        }
        
        public void FatalException(string message, Exception exception, params object[] paramList)
        {
            Console.WriteLine($"[FATAL] {_name}: {message} - {exception}");
        }
        
        public void ErrorException(string message, Exception exception, params object[] paramList)
        {
            Console.WriteLine($"[ERROR] {_name}: {message} - {exception}");
        }
        
        public void WarnException(string message, Exception exception, params object[] paramList)
        {
            Console.WriteLine($"[WARN] {_name}: {message} - {exception}");
        }
        
        public void InfoException(string message, Exception exception, params object[] paramList)
        {
            Console.WriteLine($"[INFO] {_name}: {message} - {exception}");
        }
        
        public void DebugException(string message, Exception exception, params object[] paramList)
        {
            Console.WriteLine($"[DEBUG] {_name}: {message} - {exception}");
        }
        
        public void Log(LogLevel level, string message, params object[] paramList)
        {
            Console.WriteLine($"[{level}] {_name}: {message}");
        }
        
        public void LogMultiline(string message, LogLevel level, string prefix)
        {
            Console.WriteLine($"[{level}] {_name}: {prefix} {message}");
        }
    }
} 