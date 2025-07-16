using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugins.JavScraper.Scrapers;
using Emby.Plugins.JavScraper.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugins.JavScraper
{
    public class JavImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly ImageProxyService imageProxyService;
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        public Gfriends Gfriends { get; }

        public int Order => 3;

        public JavImageProvider(IProviderManager providerManager, ILibraryManager libraryManager,
            ILogManager logManager, IJsonSerializer jsonSerializer, IApplicationPaths appPaths)
        {
            _logger = logManager.CreateLogger<JavImageProvider>();
            _jsonSerializer = jsonSerializer;

            // 从Plugin实例获取服务
            imageProxyService = Plugin.Instance.ImageProxyService;
            Gfriends = new Gfriends(logManager, jsonSerializer);
        }

        public string Name => Plugin.NAME;

        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            // 根据URL判断图片类型
            var imageType = url.Contains("/covers/") || url.Contains("/cover/") ? ImageType.Primary : ImageType.Backdrop;
            return imageProxyService.GetImageResponse(url, imageType, cancellationToken);
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item,
            LibraryOptions libraryOptions, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            var addedUrls = new HashSet<string>(); // 去重集合

            async Task<RemoteImageInfo> Add(string url, ImageType type)
            {
                // 去重检查
                if (addedUrls.Contains(url))
                {
                    _logger?.Debug($"[{item.Name}] 跳过重复图片: {url}");
                    return null;
                }

                _logger?.Info($"[{item.Name}] 添加图片: {url}, 类型: {type}");

                var img = new RemoteImageInfo()
                {
                    Type = type,
                    ProviderName = Name,
                    // 统一使用原始URL，裁剪功能通过CutImageFromOriginalUrl实现
                    Url = url
                };

                addedUrls.Add(url);
                list.Add(img);
                return img;
            }



            if (item is Movie)
            {
                JavVideoIndex index = null;
                if ((index = item.GetJavVideoIndex(_jsonSerializer)) == null)
                {
                    _logger?.Info($"{nameof(GetImages)} name:{item.Name} JavVideoIndex not found.");
                    return list;
                }

                var metadata = Plugin.Instance.db.FindMetadata(index.Provider, index.Url);
                if (metadata == null)
                    return list;

                var m = metadata?.data;

                if (string.IsNullOrWhiteSpace(m.Cover) && m.Samples?.Any() == true)
                    m.Cover = m.Samples.FirstOrDefault();

                // 获取番号作为日志标识
                var number = metadata?.data?.Num ?? item.Name;

                // 🎯 智能海报图判断逻辑
                string posterUrl = null;

                _logger?.Info($"[{number}] 🎬 开始智能海报图选择...");
                _logger?.Info($"[{number}] 📊 可用样品图数量: {m.Samples?.Count ?? 0}");
                _logger?.Info($"[{number}] 🖼️ 封面图URL: {m.Cover ?? "无"}");

                if (m.Samples?.Any() == true)
                {
                    var firstSample = m.Samples.FirstOrDefault(o => o.IsWebUrl());
                    if (!string.IsNullOrEmpty(firstSample))
                    {
                        _logger?.Info($"[{number}] 🔍 检查第一张样品图: {firstSample}");

                        // 检查第一张样品图是否为竖版
                        bool isVertical = await IsImageVertical(firstSample);

                        if (isVertical)
                        {
                            // 检查竖版样品图的质量
                            var dimensions = await GetOriginalImageDimensions(firstSample);
                            if (dimensions.HasValue)
                            {
                                int minWidth = 200; // 最小宽度要求
                                int minHeight = 300; // 最小高度要求

                                if (dimensions.Value.Width >= minWidth && dimensions.Value.Height >= minHeight)
                                {
                                    posterUrl = firstSample;
                                    _logger?.Info($"[{number}] ✅ 第一张样品图为高质量竖版，选择作为海报图: {firstSample} ({dimensions.Value.Width}x{dimensions.Value.Height})");
                                }
                                else
                                {
                                    _logger?.Info($"[{number}] ⚠️ 第一张样品图为竖版但质量较低 ({dimensions.Value.Width}x{dimensions.Value.Height})，将使用封面图代替");
                                }
                            }
                            else
                            {
                                posterUrl = firstSample;
                                _logger?.Info($"[{number}] ✅ 第一张样品图为竖版，选择作为海报图: {firstSample}");
                            }
                        }
                        else
                        {
                            _logger?.Info($"[{number}] ❌ 第一张样品图为横版，不适合作为海报图");
                            _logger?.Info($"[{number}] 🔄 将使用封面图作为海报图");
                        }
                    }
                    else
                    {
                        _logger?.Info($"[{number}] ⚠️ 第一张样品图URL无效");
                    }
                }
                else
                {
                    _logger?.Info($"[{number}] ⚠️ 没有可用的样品图");
                }

                // 如果没有找到合适的竖版样品图，使用封面图作为海报图
                if (string.IsNullOrEmpty(posterUrl))
                {
                    if (m.Cover.IsWebUrl())
                    {
                        posterUrl = m.Cover;
                        _logger?.Info($"[{number}] 📋 使用封面图作为海报图: {m.Cover}");
                    }
                    else
                    {
                        _logger?.Warn($"[{number}] ❌ 封面图URL也无效，无法设置海报图");
                    }
                }

                _logger?.Info($"[{number}] 🎯 最终选择的海报图: {posterUrl ?? "无"}");

                // 添加海报图（Primary）
                if (!string.IsNullOrEmpty(posterUrl))
                {
                    if (posterUrl == m.Cover)
                    {
                        // 当使用封面图作为海报图时，ImageProxyService会自动裁剪
                        _logger?.Info($"[{number}] 使用封面图作为海报图（将自动裁剪）");
                    }
                    else
                    {
                        // 使用样品图作为海报图，通常不需要裁剪
                        _logger?.Info($"[{number}] 使用样品图作为海报图");
                    }
                    await Add(posterUrl, ImageType.Primary);
                }

                // 添加背景图（Backdrop） - 可以选择封面图或其他样品图
                await AddBackdropImages(m, number, Add);

                // 添加缩略图（Thumb） - 所有样品图
                if (m.Samples?.Any() == true)
                {
                    int sampleIndex = 0;
                    foreach (var url in m.Samples.Where(o => o.IsWebUrl()))
                    {
                        sampleIndex++;
                        await Add(url, ImageType.Thumb);
                        // 日志已在Add方法内部输出，此处不需要重复日志
                    }
                }
            }
            else if (item is Person)
            {
                _logger?.Info($"{nameof(GetImages)} name:{item.Name}.");

                JavPersonIndex index = null;
                if ((index = item.GetJavPersonIndex(_jsonSerializer)) == null)
                {
                    var cover = await Gfriends.FindAsync(item.Name, cancellationToken);
                    _logger?.Info($"{nameof(GetImages)} name:{item.Name} Gfriends: {cover}.");

                    if (cover.IsWebUrl() != true)
                        return list;

                    index = new JavPersonIndex() { Cover = cover };
                }

                if (!index.Cover.IsWebUrl())
                {
                    index.Cover = await Gfriends.FindAsync(item.Name, cancellationToken);
                    if (string.IsNullOrWhiteSpace(index.Cover))
                        return list;
                }

                if (index.Cover.IsWebUrl())
                {
                    await Add(index.Cover, ImageType.Primary);
                    await Add(index.Cover, ImageType.Backdrop);
                }

                if (index.Samples?.Any() == true)
                {
                    foreach (var url in index.Samples.Where(o => o.IsWebUrl()))
                        await Add(url, ImageType.Thumb);
                }
            }

            return list;
        }

        /// <summary>
        /// 检查图片是否为竖版（高度大于宽度）- 检测原始图片尺寸，不经过裁剪
        /// </summary>
        private async Task<bool> IsImageVertical(string imageUrl)
        {
            try
            {
                _logger?.Info($"🔍 检查原始图片尺寸: {imageUrl}");

                // 直接下载图片头部数据，不经过CutImage处理
                var originalDimensions = await GetOriginalImageDimensions(imageUrl);
                if (originalDimensions.HasValue)
                {
                    bool isVertical = originalDimensions.Value.Height > originalDimensions.Value.Width;
                    double ratio = (double)originalDimensions.Value.Height / originalDimensions.Value.Width;
                    _logger?.Info($"📐 原始图片尺寸: {originalDimensions.Value.Width}x{originalDimensions.Value.Height}, 高宽比: {ratio:F2}, 竖版: {isVertical}");
                    return isVertical;
                }
                else
                {
                    _logger?.Warn($"❌ 无法获取原始图片尺寸: {imageUrl}");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn($"❌ 图片尺寸检测失败: {imageUrl}, 错误: {ex.Message}");
            }

            // 检测失败时，尝试通过URL推测（快速备用方案）
            return GuessOrientationFromUrl(imageUrl);
        }

        /// <summary>
        /// 获取原始图片尺寸，不经过裁剪处理
        /// </summary>
        private async Task<(int Width, int Height)?> GetOriginalImageDimensions(string imageUrl)
        {
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    // 使用HttpClient直接下载图片头部，避免经过CutImage处理
                    using (var httpClient = new System.Net.Http.HttpClient())
                    {
                        httpClient.Timeout = TimeSpan.FromSeconds(10);
                        httpClient.DefaultRequestHeaders.Add("User-Agent",
                            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                        // 只请求前2KB数据用于尺寸检测
                        httpClient.DefaultRequestHeaders.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 2047);

                        var response = await httpClient.GetAsync(imageUrl, cts.Token);
                        if (response.IsSuccessStatusCode)
                        {
                            var imageData = await response.Content.ReadAsByteArrayAsync();
                            _logger?.Info($"📥 下载原始图片头部数据: {imageData.Length} 字节");

                            return GetImageDimensions(imageData);
                        }
                        else
                        {
                            _logger?.Warn($"❌ HTTP请求失败: {response.StatusCode}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn($"❌ 获取原始图片尺寸失败: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 通过URL推测图片方向（备用方案）
        /// </summary>
        private bool GuessOrientationFromUrl(string imageUrl)
        {
            try
            {
                // 一些常见的竖版图片URL特征
                var verticalIndicators = new[] { "_portrait", "_vertical", "_tall", "_v" };
                var horizontalIndicators = new[] { "_landscape", "_horizontal", "_wide", "_h" };

                var lowerUrl = imageUrl.ToLower();

                if (verticalIndicators.Any(indicator => lowerUrl.Contains(indicator)))
                {
                    _logger?.Info($"🔍 通过URL推测为竖版: {imageUrl}");
                    return true;
                }

                if (horizontalIndicators.Any(indicator => lowerUrl.Contains(indicator)))
                {
                    _logger?.Info($"🔍 通过URL推测为横版: {imageUrl}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"URL推测失败: {ex.Message}");
            }

            return false; // 默认横版
        }

        /// <summary>
        /// 从图片字节数据中获取尺寸信息
        /// </summary>
        private (int Width, int Height)? GetImageDimensions(byte[] imageData)
        {
            try
            {
                if (imageData.Length < 24) return null;

                // JPEG格式检测
                if (imageData[0] == 0xFF && imageData[1] == 0xD8)
                {
                    return GetJpegDimensions(imageData);
                }

                // PNG格式检测
                if (imageData[0] == 0x89 && imageData[1] == 0x50 && imageData[2] == 0x4E && imageData[3] == 0x47)
                {
                    return GetPngDimensions(imageData);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"解析图片尺寸失败: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 获取JPEG图片尺寸
        /// </summary>
        private (int Width, int Height)? GetJpegDimensions(byte[] imageData)
        {
            int pos = 2;
            while (pos < imageData.Length - 1)
            {
                if (imageData[pos] != 0xFF) break;

                byte marker = imageData[pos + 1];
                pos += 2;

                if (marker == 0xC0 || marker == 0xC2) // SOF0 or SOF2
                {
                    if (pos + 6 < imageData.Length)
                    {
                        int height = (imageData[pos + 3] << 8) | imageData[pos + 4];
                        int width = (imageData[pos + 5] << 8) | imageData[pos + 6];
                        return (width, height);
                    }
                }
                else
                {
                    if (pos + 1 < imageData.Length)
                    {
                        int length = (imageData[pos] << 8) | imageData[pos + 1];
                        pos += length;
                    }
                    else break;
                }
            }
            return null;
        }

        /// <summary>
        /// 获取PNG图片尺寸
        /// </summary>
        private (int Width, int Height)? GetPngDimensions(byte[] imageData)
        {
            if (imageData.Length >= 24)
            {
                int width = (imageData[16] << 24) | (imageData[17] << 16) | (imageData[18] << 8) | imageData[19];
                int height = (imageData[20] << 24) | (imageData[21] << 16) | (imageData[22] << 8) | imageData[23];
                return (width, height);
            }
            return null;
        }

        /// <summary>
        /// 添加背景图 - 优先选择高质量的横版图片
        /// </summary>
        private async Task AddBackdropImages(JavVideo m, string number, Func<string, ImageType, Task<RemoteImageInfo>> addFunc)
        {
            var backdropImages = new List<string>();

            // 1. 优先使用封面图作为背景图
            if (m.Cover.IsWebUrl())
            {
                backdropImages.Add(m.Cover);
                _logger?.Info($"[{number}] 添加封面图作为背景图: {m.Cover}");
            }

            // 2. 添加所有样品图作为背景图
            if (m.Samples?.Any() == true)
            {
                int backdropCount = 0;
                foreach (var sampleUrl in m.Samples.Where(o => o.IsWebUrl()))
                {
                    try
                    {
                        var dimensions = await GetOriginalImageDimensions(sampleUrl);
                        if (dimensions.HasValue)
                        {
                            // 选择横版且尺寸较大的图片作为背景图
                            bool isHorizontal = dimensions.Value.Width > dimensions.Value.Height;
                            bool isLargeEnough = dimensions.Value.Width >= 600; // 宽度至少600px

                            if (isHorizontal && isLargeEnough)
                            {
                                backdropImages.Add(sampleUrl);
                                backdropCount++;
                                _logger?.Info($"[{number}] 添加样品图作为背景图 {backdropCount}: {sampleUrl} ({dimensions.Value.Width}x{dimensions.Value.Height})");
                            }
                            else
                            {
                                _logger?.Info($"[{number}] 跳过样品图（不符合背景图要求）: {sampleUrl} ({dimensions.Value.Width}x{dimensions.Value.Height})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warn($"[{number}] 检查样品图尺寸失败: {sampleUrl}, {ex.Message}");
                    }
                }

                _logger?.Info($"[{number}] 总共添加了 {backdropCount} 张样品图作为背景图");
            }

            // 添加所有背景图
            foreach (var backdropUrl in backdropImages)
            {
                await addFunc(backdropUrl, ImageType.Backdrop);
            }
        }

        public System.Collections.Generic.IEnumerable<ImageType> GetSupportedImages(BaseItem item)
               => new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Thumb };

        public bool Supports(BaseItem item) => item is Movie || item is Person;
    }
}