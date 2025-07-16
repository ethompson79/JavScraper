using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ScraperIntegrationTest
{
    // 模拟JavVideo数据结构
    public class MockJavVideo
    {
        public string Provider { get; set; }
        public string Url { get; set; }
        public string Num { get; set; }
        public string Title { get; set; }
        public string Plot { get; set; }
        public string Cover { get; set; }
        public string Director { get; set; }
        public string Studio { get; set; }
        public string Maker { get; set; }
        public string Set { get; set; }
        public string Runtime { get; set; }
        public List<string> Genres { get; set; }
        public List<string> Actors { get; set; }
        public List<string> Samples { get; set; }
        public double? CommunityRating { get; set; }
    }

    // 模拟刮削器配置
    public class MockScraperConfig
    {
        public string Name { get; set; }
        public bool Enable { get; set; }
    }

    // 模拟刮削器
    public interface IMockScraper
    {
        string Name { get; }
        Task<MockJavVideo> GetData(string id);
    }

    public class MockJavBusScraper : IMockScraper
    {
        public string Name => "JavBus";

        public async Task<MockJavVideo> GetData(string id)
        {
            await Task.Delay(100); // 模拟网络延迟

            // 模拟JavBus可能的数据情况
            if (id == "PRED-066")
            {
                return new MockJavVideo
                {
                    Provider = "JavBus",
                    Num = "PRED-066",
                    Title = "", // JavBus标题为空的情况
                    Plot = "", // JavBus简介为空
                    Cover = "https://javbus.com/cover/pred-066.jpg",
                    Director = "Director A",
                    Studio = "Studio JavBus",
                    Maker = "Maker JavBus",
                    Set = "",
                    Runtime = "120分钟",
                    Genres = new List<string> { "类别1", "类别2" },
                    Actors = new List<string> { "演员A", "演员B" },
                    Samples = new List<string> { "sample1.jpg", "sample2.jpg" },
                    CommunityRating = 8.5
                };
            }
            return null; // 模拟找不到数据
        }
    }

    public class MockJavDBScraper : IMockScraper
    {
        public string Name => "JavDB";

        public async Task<MockJavVideo> GetData(string id)
        {
            await Task.Delay(150); // 模拟网络延迟

            // 模拟JavDB可能的数据情况
            if (id == "PRED-066")
            {
                return new MockJavVideo
                {
                    Provider = "JavDB",
                    Num = "PRED-066",
                    Title = "部活合宿NTR ～バレー部の彼女と絶倫部員の最低な浮気中出し映像～", // JavDB有完整标题
                    Plot = "这是一个详细的剧情简介...", // JavDB有详细简介
                    Cover = "https://javdb.com/cover/pred-066.jpg",
                    Director = "", // JavDB导演信息为空
                    Studio = "Studio JavDB",
                    Maker = "", // JavDB发行商为空
                    Set = "NTR系列",
                    Runtime = "", // JavDB时长为空
                    Genres = new List<string> { "类别2", "类别3", "类别4" }, // 部分重复，部分新增
                    Actors = new List<string> { "演员B", "演员C" }, // 部分重复，部分新增
                    Samples = new List<string> { "sample3.jpg", "sample4.jpg" }, // 不同的样片
                    CommunityRating = null // JavDB没有评分
                };
            }
            return null;
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== 测试刮削器数据互补逻辑 ===");
            Console.WriteLine("模拟场景：JavBus优先，JavDB补充缺失数据");
            Console.WriteLine();

            // 模拟配置：两个刮削器都启用
            var enabledScrapers = new List<MockScraperConfig>
            {
                new MockScraperConfig { Name = "JavBus", Enable = true },
                new MockScraperConfig { Name = "JavDB", Enable = true }
            };

            // 创建刮削器实例
            var scrapers = new List<IMockScraper>
            {
                new MockJavBusScraper(),
                new MockJavDBScraper()
            };

            // 测试番号
            var testId = "PRED-066";
            
            await TestScraperIntegration(enabledScrapers, scrapers, testId);
            
            Console.WriteLine("\n测试完成。按任意键退出...");
            Console.ReadKey();
        }

        static async Task TestScraperIntegration(List<MockScraperConfig> enabledScrapers, List<IMockScraper> scrapers, string testId)
        {
            Console.WriteLine($"搜索番号: {testId}");
            Console.WriteLine();

            // 1. 按优先级排序（JavBus优先）
            var orderedScrapers = new List<MockScraperConfig>();
            
            // 首先添加JavBus（如果启用）
            var javBus = enabledScrapers.FirstOrDefault(s => s.Name == "JavBus");
            if (javBus != null)
            {
                orderedScrapers.Add(javBus);
            }
            
            // 然后添加其他刮削器
            var otherScrapers = enabledScrapers.Where(s => s.Name != "JavBus").ToList();
            orderedScrapers.AddRange(otherScrapers);
            
            Console.WriteLine($"刮削器优先级顺序: {string.Join(", ", orderedScrapers.Select(s => s.Name))}");
            Console.WriteLine();

            // 2. 尝试按优先级获取主要数据
            MockJavVideo primaryData = null;
            string primaryProvider = null;

            foreach (var scraperConfig in orderedScrapers)
            {
                var scraper = scrapers.FirstOrDefault(s => s.Name == scraperConfig.Name);
                if (scraper == null) continue;

                Console.WriteLine($"尝试从 {scraper.Name} 获取主要数据...");
                
                try
                {
                    var data = await scraper.GetData(testId);
                    if (data != null)
                    {
                        primaryData = data;
                        primaryProvider = scraper.Name;
                        Console.WriteLine($"✓ 成功从 {scraper.Name} 获取主要数据");
                        break;
                    }
                    else
                    {
                        Console.WriteLine($"✗ {scraper.Name} 没有找到数据");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ {scraper.Name} 获取数据时出错: {ex.Message}");
                }
            }

            if (primaryData == null)
            {
                Console.WriteLine("没有找到任何主要数据");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("=== 主要数据 ===");
            PrintJavVideoData(primaryData);

            // 3. 数据互补
            if (orderedScrapers.Count > 1)
            {
                Console.WriteLine();
                Console.WriteLine("=== 开始数据互补 ===");
                await SupplementData(primaryData, orderedScrapers, scrapers, primaryProvider);
            }

            Console.WriteLine();
            Console.WriteLine("=== 最终合并数据 ===");
            PrintJavVideoData(primaryData);
        }

        static async Task SupplementData(MockJavVideo primaryData, List<MockScraperConfig> orderedScrapers, List<IMockScraper> scrapers, string primaryProvider)
        {
            foreach (var scraperConfig in orderedScrapers)
            {
                if (scraperConfig.Name == primaryProvider) continue; // 跳过主提供者

                var scraper = scrapers.FirstOrDefault(s => s.Name == scraperConfig.Name);
                if (scraper == null) continue;

                Console.WriteLine($"尝试从 {scraper.Name} 补充数据...");

                try
                {
                    var supplementData = await scraper.GetData("PRED-066");
                    if (supplementData != null)
                    {
                        bool hasChanges = false;

                        // 补充标题
                        if (string.IsNullOrWhiteSpace(primaryData.Title) && !string.IsNullOrWhiteSpace(supplementData.Title))
                        {
                            Console.WriteLine($"  ✓ 补充标题: '{supplementData.Title}'");
                            primaryData.Title = supplementData.Title;
                            hasChanges = true;
                        }

                        // 补充简介
                        if (string.IsNullOrWhiteSpace(primaryData.Plot) && !string.IsNullOrWhiteSpace(supplementData.Plot))
                        {
                            Console.WriteLine($"  ✓ 补充简介: '{supplementData.Plot.Substring(0, Math.Min(20, supplementData.Plot.Length))}...'");
                            primaryData.Plot = supplementData.Plot;
                            hasChanges = true;
                        }

                        // 补充导演
                        if (string.IsNullOrWhiteSpace(primaryData.Director) && !string.IsNullOrWhiteSpace(supplementData.Director))
                        {
                            Console.WriteLine($"  ✓ 补充导演: '{supplementData.Director}'");
                            primaryData.Director = supplementData.Director;
                            hasChanges = true;
                        }

                        // 补充系列
                        if (string.IsNullOrWhiteSpace(primaryData.Set) && !string.IsNullOrWhiteSpace(supplementData.Set))
                        {
                            Console.WriteLine($"  ✓ 补充系列: '{supplementData.Set}'");
                            primaryData.Set = supplementData.Set;
                            hasChanges = true;
                        }

                        // 补充时长
                        if (string.IsNullOrWhiteSpace(primaryData.Runtime) && !string.IsNullOrWhiteSpace(supplementData.Runtime))
                        {
                            Console.WriteLine($"  ✓ 补充时长: '{supplementData.Runtime}'");
                            primaryData.Runtime = supplementData.Runtime;
                            hasChanges = true;
                        }

                        // 补充类别（合并唯一值）
                        if (supplementData.Genres?.Any() == true)
                        {
                            if (primaryData.Genres == null) primaryData.Genres = new List<string>();
                            var newGenres = supplementData.Genres.Where(g => !primaryData.Genres.Contains(g, StringComparer.OrdinalIgnoreCase)).ToList();
                            if (newGenres.Any())
                            {
                                Console.WriteLine($"  ✓ 添加类别: {string.Join(", ", newGenres)}");
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
                                Console.WriteLine($"  ✓ 添加演员: {string.Join(", ", newActors)}");
                                primaryData.Actors.AddRange(newActors);
                                hasChanges = true;
                            }
                        }

                        // 补充样片（合并唯一值）
                        if (supplementData.Samples?.Any() == true)
                        {
                            if (primaryData.Samples == null) primaryData.Samples = new List<string>();
                            var newSamples = supplementData.Samples.Where(s => !primaryData.Samples.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
                            if (newSamples.Any())
                            {
                                Console.WriteLine($"  ✓ 添加样片: {newSamples.Count} 个");
                                primaryData.Samples.AddRange(newSamples);
                                hasChanges = true;
                            }
                        }

                        if (!hasChanges)
                        {
                            Console.WriteLine($"  - 没有需要补充的数据");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  ✗ {scraper.Name} 没有找到补充数据");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ✗ 从 {scraper.Name} 补充数据时出错: {ex.Message}");
                }
            }
        }

        static void PrintJavVideoData(MockJavVideo data)
        {
            Console.WriteLine($"  提供者: {data.Provider}");
            Console.WriteLine($"  番号: {data.Num}");
            Console.WriteLine($"  标题: {(string.IsNullOrWhiteSpace(data.Title) ? "[空]" : data.Title)}");
            Console.WriteLine($"  简介: {(string.IsNullOrWhiteSpace(data.Plot) ? "[空]" : data.Plot.Substring(0, Math.Min(30, data.Plot.Length)) + "...")}");
            Console.WriteLine($"  导演: {(string.IsNullOrWhiteSpace(data.Director) ? "[空]" : data.Director)}");
            Console.WriteLine($"  制作商: {(string.IsNullOrWhiteSpace(data.Studio) ? "[空]" : data.Studio)}");
            Console.WriteLine($"  系列: {(string.IsNullOrWhiteSpace(data.Set) ? "[空]" : data.Set)}");
            Console.WriteLine($"  时长: {(string.IsNullOrWhiteSpace(data.Runtime) ? "[空]" : data.Runtime)}");
            Console.WriteLine($"  类别: {(data.Genres?.Any() == true ? string.Join(", ", data.Genres) : "[空]")}");
            Console.WriteLine($"  演员: {(data.Actors?.Any() == true ? string.Join(", ", data.Actors) : "[空]")}");
            Console.WriteLine($"  样片数量: {data.Samples?.Count ?? 0}");
            Console.WriteLine($"  评分: {data.CommunityRating?.ToString() ?? "[空]"}");
        }
    }
} 