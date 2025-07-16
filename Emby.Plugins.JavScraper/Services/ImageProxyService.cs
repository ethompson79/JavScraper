using Emby.Plugins.JavScraper.Data;
using Emby.Plugins.JavScraper.Http;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;

using MediaBrowser.Model.Logging;

using MediaBrowser.Model.Serialization;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using System.Web;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Emby.Plugins.JavScraper.Services
{
    /// <summary>
    /// 图片代理服务 - 融合反爬虫和智能处理增强版
    /// </summary>
    public class ImageProxyService : IDisposable
    {
        private HttpClientEx client;
        private static FileExtensionContentTypeProvider fileExtensionContentTypeProvider = new FileExtensionContentTypeProvider();
        
        // 反爬虫增强配置 - 融合测试工具验证的功能（线程安全）
        private static readonly ThreadLocal<Random> random = new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));
        private readonly string[] userAgents = {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/121.0",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        };
        
        // 并发控制 - 避免过多同时下载
        private readonly SemaphoreSlim downloadSemaphore = new SemaphoreSlim(8, 8);
        private readonly ConcurrentDictionary<string, DateTime> lastRequestTime = new ConcurrentDictionary<string, DateTime>();
        
        /// <summary>
        /// 是否已释放资源
        /// </summary>
        private bool disposed = false;

        public ImageProxyService(IServerApplicationHost serverApplicationHost, IJsonSerializer jsonSerializer, ILogger logger, IFileSystem fileSystem, IApplicationPaths appPaths)
        {
            client = new HttpClientEx();
            this.serverApplicationHost = serverApplicationHost;
            this.jsonSerializer = jsonSerializer;
            this.logger = logger;
            this.fileSystem = fileSystem;
            this.appPaths = appPaths;
        }

        private readonly IServerApplicationHost serverApplicationHost;
        private readonly IJsonSerializer jsonSerializer;
        private readonly ILogger logger;
        private readonly IFileSystem fileSystem;
        private readonly IApplicationPaths appPaths;

        /// <summary>
        /// 构造本地url地址
        /// </summary>
        /// <param name="url"></param>
        /// <param name="type"></param>
        /// <param name="with_api_url">是否包含 api url</param>
        /// <returns></returns>
        public async Task<string> GetLocalUrl(string url, ImageType type = ImageType.Backdrop, bool with_api_url = true)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            if (url.IndexOf("Plugins/JavScraper/Image", StringComparison.OrdinalIgnoreCase) >= 0)
                return url;

            var api_url = with_api_url ? await serverApplicationHost.GetLocalApiUrl(default(CancellationToken)) : string.Empty;
            // 使用不需要认证的路径
            return $"{api_url}/Plugins/JavScraper/Image?url={WebUtility.UrlEncode(url)}&type={type}";
        }

        /// <summary>
        /// 获取图片
        /// </summary>
        /// <param name="url">地址</param>
        /// <param name="type">类型</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<HttpResponseInfo> GetImageResponse(string url, ImageType type, CancellationToken cancellationToken)
        {
            try
            {
                logger?.Info($"GetImageResponse: Starting request for URL: {url}, Type: {type}");
                
            //  /emby/Plugins/JavScraper/Image?url=&type=xx
            if (url.IndexOf("Plugins/JavScraper/Image", StringComparison.OrdinalIgnoreCase) >= 0) //本地的链接
            {
                    try
                    {
                        // 处理相对URL，添加基础URL
                        Uri uri;
                        if (url.StartsWith("/"))
                        {
                            uri = new Uri("http://localhost" + url);
                        }
                        else if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
                        {
                            logger?.Error($"GetImageResponse: Invalid URL format: {url}");
                            return CreateEmptyResponse();
                        }
                        
                        var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var url2 = q["url"];
                if (url2.IsWebUrl())
                {
                    url = url2;
                    var tt = q.Get("type");
                    if (!string.IsNullOrWhiteSpace(tt) && Enum.TryParse<ImageType>(tt.Trim(), out var t2))
                        type = t2;
                }
                    }
                    catch (Exception ex)
                    {
                        logger?.Error($"GetImageResponse: Error parsing URL {url}: {ex.Message}");
                        return CreateEmptyResponse();
                    }
            }

                logger?.Info($"GetImageResponse: Processing URL: {url}");

            var key = WebUtility.UrlEncode(url);
            var cache_file = Path.Combine(appPaths.GetImageCachePath().ToString(), key);
            byte[] bytes = null;

            //尝试从缓存中读取
            try
            {
                var fi = fileSystem.GetFileInfo(cache_file);

                //图片文件存在，且是24小时之内的
                    if (fi.Exists && fi.LastWriteTimeUtc > DateTime.Now.AddDays(-1).ToUniversalTime())
                {
                        logger?.Info($"GetImageResponse: Reading from cache: {cache_file}");
                    bytes = await fileSystem.ReadAllBytesAsync(cache_file);
                    logger?.Info($"GetImageResponse: Hit image cache {url} {cache_file}, size: {bytes?.Length ?? 0}");

                    if (bytes != null && bytes.Length > 0)
                    {
                        if (type == ImageType.Primary && IsCoverImage(url))
                        {
                            var ci = await CutImage(bytes, url);
                            if (ci != null)
                                return ci;
                        }

                        fileExtensionContentTypeProvider.TryGetContentType(url, out var contentType);

                        return new HttpResponseInfo()
                        {
                            Content = new MemoryStream(bytes),
                            ContentLength = bytes.Length,
                            ContentType = contentType ?? "image/jpeg",
                            StatusCode = HttpStatusCode.OK,
                        };
                        }
                        else
                        {
                            logger?.Warn($"GetImageResponse: Cache file is empty or corrupted: {cache_file}");
                        }
                    }
                    else
                    {
                        if (fi.Exists)
                        {
                            logger?.Info($"GetImageResponse: Cache file expired: {cache_file}");
                        }
                        else
                        {
                            logger?.Info($"GetImageResponse: Cache file not found: {cache_file}");
                        }
                }
            }
            catch (Exception ex)
            {
                    logger?.Warn($"GetImageResponse: Read image cache error. {url} {cache_file} {ex.Message}");
            }

            try
            {
                    // 确保URL是绝对路径
                    if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                    {
                        logger?.Error($"GetImageResponse: Invalid URL format: {url}");
                        return CreateEmptyResponse();
                    }
                    
                    logger?.Info($"GetImageResponse: Downloading from remote URL: {url}");
                
                // 使用增强的图片下载方法 - 融合测试工具验证的反爬虫策略
                var downloadResult = await DownloadImageWithAntiBot(url, cancellationToken);
                if (!downloadResult.success)
                {
                    logger?.Warn($"GetImageResponse: Enhanced download failed for URL: {url}");
                    return CreateEmptyResponse();
                }
                
                byte[] imageBytes = downloadResult.imageBytes;
                    
                    if (imageBytes != null && imageBytes.Length > 0)
                    {
                        logger?.Info($"GetImageResponse: Downloaded {imageBytes.Length} bytes from {url}");
                        
                        // 安全地保存到缓存
                        try
                        {
                            // 确保缓存目录存在
                            var cacheDir = appPaths.GetImageCachePath().ToString();
                            if (!fileSystem.DirectoryExists(cacheDir))
                            {
                                fileSystem.CreateDirectory(cacheDir);
                            }
                            
                            fileSystem.WriteAllBytes(cache_file, imageBytes);
                            logger?.Info($"GetImageResponse: Save image cache {url} {cache_file}");
                        }
                        catch (Exception ex)
                        {
                            logger?.Warn($"GetImageResponse: Save image cache error. {url} {cache_file} {ex.Message}");
                            // 缓存失败不影响继续处理
                        }
                    }
                    else
                    {
                        logger?.Warn($"GetImageResponse: Downloaded empty content from {url}");
                    }

                    if (type == ImageType.Primary && IsCoverImage(url) && imageBytes != null && imageBytes.Length > 0)
                    {
                        try
                        {
                            var ci = await CutImage(imageBytes, url);
                            if (ci != null)
                                return ci;
                        }
                        catch (Exception ex)
                        {
                            logger?.Warn($"GetImageResponse: Image cutting failed for {url}: {ex.Message}");
                            // 切图失败，继续使用原图
                        }
                    }

                    // 如果已读取字节数据，直接使用，否则从响应解析
                    if (imageBytes != null && imageBytes.Length > 0)
                    {
                        fileExtensionContentTypeProvider.TryGetContentType(url, out var contentType);
                        return new HttpResponseInfo()
                        {
                            Content = new MemoryStream(imageBytes),
                            ContentLength = imageBytes.Length,
                            ContentType = contentType ?? "image/jpeg",
                            StatusCode = HttpStatusCode.OK,
                        };
                    }

                    logger?.Warn($"GetImageResponse: No valid image data received from {url}");
                    return CreateEmptyResponse();
                }
                catch (Exception ex)
                {
                    logger?.Error($"GetImageResponse: HTTP request error for {url}: {ex.Message}");
                    logger?.Error($"GetImageResponse: Stack trace: {ex.StackTrace}");
                    return CreateEmptyResponse();
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"GetImageResponse: Unexpected error for {url}: {ex.Message}");
                logger?.Error($"GetImageResponse: Stack trace: {ex.StackTrace}");
                return CreateEmptyResponse();
            }
        }
        
        /// <summary>
        /// 创建空响应
        /// </summary>
        /// <returns></returns>
        private HttpResponseInfo CreateEmptyResponse()
        {
            return new HttpResponseInfo()
            {
                Content = new MemoryStream(),
                ContentLength = 0,
                ContentType = "image/jpeg",
                StatusCode = HttpStatusCode.NotFound,
            };
        }
        
        /// <summary>
        /// 增强版图片下载方法 - 融合测试工具验证的反爬虫策略
        /// </summary>
        private async Task<(bool success, byte[] imageBytes)> DownloadImageWithAntiBot(string imageUrl, CancellationToken cancellationToken)
        {
            await downloadSemaphore.WaitAsync(cancellationToken); // 控制并发
            try
            {
                // 智能延迟避免限流
                await SmartDelayForDomain(imageUrl);
                
                using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
                
                // 智能防盗链处理 - 增强版
                if (imageUrl.Contains("javbus") || imageUrl.Contains("buscdn") || imageUrl.Contains("javbus22"))
                {
                    request.Headers.Add("Referer", "https://www.javbus.com/");
                    logger?.Debug($"ImageProxy: 设置JavBus Referer");
                }
                else if (imageUrl.Contains("javdb") || imageUrl.Contains("jdbstatic"))
                {
                    request.Headers.Add("Referer", "https://javdb.com/");
                    logger?.Debug($"ImageProxy: 设置JavDB Referer");
                }
                else
                {
                    request.Headers.Add("Referer", "https://www.google.com/");
                    logger?.Debug($"ImageProxy: 设置通用Referer");
                }
                
                // 轮换User-Agent增强真实性
                var imageUserAgent = userAgents.ElementAt(random.Value.Next(userAgents.Length));
                request.Headers.Add("User-Agent", imageUserAgent);
                
                // 添加更多真实浏览器请求头
                request.Headers.Add("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
                request.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
                request.Headers.Add("Sec-Fetch-Dest", "image");
                request.Headers.Add("Sec-Fetch-Mode", "no-cors");
                request.Headers.Add("Sec-Fetch-Site", "cross-site");
                
                // 随机添加一些可选请求头增强真实性
                if (random.Value.Next(2) == 0)
                {
                    request.Headers.Add("DNT", "1");
                }
                if (random.Value.Next(3) == 0)
                {
                    request.Headers.Add("Cache-Control", "no-cache");
                }
                
                // 发送请求
                var response = await client.GetClient().SendAsync(request, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    logger?.Debug($"ImageProxy: 下载成功: {Path.GetFileName(imageUrl)} ({imageBytes.Length} bytes)");
                    return (true, imageBytes);
                }
                else
                {
                    logger?.Warn($"ImageProxy: 下载失败: {response.StatusCode} - {Path.GetFileName(imageUrl)}");
                    return (false, null);
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"ImageProxy: 下载错误: {ex.Message} - {Path.GetFileName(imageUrl)}");
                return (false, null);
            }
            finally
            {
                downloadSemaphore.Release();
            }
        }
        
        /// <summary>
        /// 智能域名延迟，避免被限流
        /// </summary>
        private async Task SmartDelayForDomain(string url)
        {
            try
            {
                var uri = new Uri(url);
                var domain = uri.Host;
                
                if (lastRequestTime.TryGetValue(domain, out DateTime lastTime))
                {
                    var elapsed = DateTime.Now - lastTime;
                    var minDelay = TimeSpan.FromMilliseconds(200 + random.Value.Next(100, 300)); // 0.2-0.5秒随机延迟
                    
                    if (elapsed < minDelay)
                    {
                        var delayTime = minDelay - elapsed;
                        logger?.Debug($"ImageProxy: 智能延迟 {delayTime.TotalMilliseconds:F0}ms 避免限流");
                        await Task.Delay(delayTime);
                    }
                }
                lastRequestTime[domain] = DateTime.Now;
            }
            catch (Exception ex)
            {
                logger?.Debug($"ImageProxy: 延迟计算错误: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 极速优化的图片尺寸检测方法 - 融合测试工具验证的功能
        /// </summary>
        private (int width, int height) GetImageDimensionsOptimized(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length < 10)
            {
                return (1920, 1080); // 快速返回默认值
            }
            
            try
            {
                // 优化：只检查文件头部分，提高性能
                if (imageBytes.Length > 10 && imageBytes[0] == 0xFF && imageBytes[1] == 0xD8)
                {
                    return GetJpegDimensionsUltraFast(imageBytes);
                }
                // PNG检测优化
                else if (imageBytes.Length > 24 && 
                         imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && 
                         imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
                {
                    return GetPngDimensionsUltraFast(imageBytes);
                }
            }
            catch (Exception ex)
            {
                logger?.Debug($"ImageProxy: 图片尺寸检测失败: {ex.Message}");
            }
            
            // 默认假设为横版
            return (1920, 1080);
        }
        
        /// <summary>
        /// 超快速JPEG尺寸检测
        /// </summary>
        private (int width, int height) GetJpegDimensionsUltraFast(byte[] imageBytes)
        {
            try
            {
                // 超级优化：限制搜索范围到前512字节，提高性能
                int searchLimit = Math.Min(imageBytes.Length - 8, 512);
                for (int i = 2; i < searchLimit; i++)
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
        
        /// <summary>
        /// 超快速PNG尺寸检测
        /// </summary>
        private (int width, int height) GetPngDimensionsUltraFast(byte[] imageBytes)
        {
            try
            {
                if (imageBytes.Length >= 24)
                {
                    int width = (imageBytes[16] << 24) | (imageBytes[17] << 16) | (imageBytes[18] << 8) | imageBytes[19];
                    int height = (imageBytes[20] << 24) | (imageBytes[21] << 16) | (imageBytes[22] << 8) | imageBytes[23];
                    return (width, height);
                }
            }
            catch { }
            return (1920, 1080); // 默认值
        }
        
        /// <summary>
        /// 智能海报识别方法 - 融合测试工具验证的逻辑
        /// </summary>
        public bool IsPortraitImage(byte[] imageBytes)
        {
            var (width, height) = GetImageDimensionsOptimized(imageBytes);
            bool isPortrait = height > width; // 竖版图片判断
            logger?.Debug($"ImageProxy: 图片尺寸检测 {width}x{height} {(isPortrait ? "(竖版-海报)" : "(横版-封面)")}");
            return isPortrait;
        }
        
        /// <summary>
        /// 创建一个1x1像素的透明JPEG图片
        /// </summary>
        /// <returns></returns>
        private byte[] CreateTransparentPixelImage()
        {
            try
            {
                using (var bitmap = new SKBitmap(1, 1))
                {
                    bitmap.SetPixel(0, 0, SKColors.Transparent);
                    using (var image = SKImage.FromBitmap(bitmap))
                    {
                        using (var encodedData = image.Encode(SKEncodedImageFormat.Jpeg, 50))
                        {
                            return encodedData.ToArray();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"CreateTransparentPixelImage: Error creating transparent pixel: {ex.Message}");
                // 如果创建图片失败，返回最小的JPEG头
                return new byte[] 
                {
                    0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 
                    0x00, 0x01, 0x01, 0x01, 0x00, 0x48, 0x00, 0x48, 0x00, 0x00, 
                    0xFF, 0xD9
                };
            }
        }

        /// <summary>
        /// 判断是否为封面图（需要裁剪的图片）
        /// </summary>
        /// <param name="url">图片URL</param>
        /// <returns>true表示是封面图，需要裁剪</returns>
        private bool IsCoverImage(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            // 封面图通常包含 "covers" 路径，样品图通常包含 "samples" 路径
            // JavDB: https://c0.jdbstatic.com/covers/xxx.jpg (封面图)
            // JavDB: https://c0.jdbstatic.com/samples/xxx.jpg (样品图)
            // JavBus: https://www.javbus.com/pics/cover/xxx.jpg (封面图)
            // JavBus: https://www.javbus.com/pics/thumb/xxx.jpg (样品图)

            bool isCover = url.Contains("/covers/") || url.Contains("/cover/");
            bool isSample = url.Contains("/samples/") || url.Contains("/thumb/");

            logger?.Info($"IsCoverImage: URL={url}, isCover={isCover}, isSample={isSample}");

            // 如果明确是样品图，则不裁剪
            if (isSample)
                return false;

            // 如果明确是封面图，则裁剪
            if (isCover)
                return true;

            // 默认情况下，对于Primary类型的图片，如果无法确定类型，则不裁剪
            // 这样可以避免错误地裁剪样品图
            return false;
        }

        /// <summary>
        /// 剪裁图片
        /// </summary>
        /// <param name="bytes">图片内容</param>
        /// <returns>为空：剪裁失败或者不需要剪裁。</returns>
        /// <param name="url">图片地址</param>
        private async Task<HttpResponseInfo> CutImage(byte[] bytes, string url = null)
        {
            logger?.Info($"CutImage: Starting image processing for URL: {url}");
            
            if (bytes == null || bytes.Length == 0)
            {
                logger?.Warn($"CutImage: Image bytes are null or empty for URL: {url}");
                return null;
            }
            
            try
            {
                using (var ms = new MemoryStream(bytes))
                {
                    ms.Position = 0;
                    logger?.Info($"CutImage: Processing image of {bytes.Length} bytes");
                    
                    using (var inputStream = new SKManagedStream(ms))
                    {
                        using (var bitmap = SKBitmap.Decode(inputStream))
                        {
                            if (bitmap == null)
                            {
                                logger?.Warn($"CutImage: Failed to decode image for URL: {url}");
                                return null;
                            }
                            
                            var h = bitmap.Height;
                            var w = bitmap.Width;
                            var w2 = h * 2 / 3; //封面宽度

                            logger?.Info($"CutImage: Image dimensions: {w}x{h}, target width: {w2}");

                            if (w2 < w) //需要剪裁
                            {
                                logger?.Info($"CutImage: Image needs cutting. Original: {w}x{h}, Target: {w2}x{h}");
                                
                                var x = await GetBaiduBodyAnalysisResult(bytes, url);
                                var start_w = w - w2; //默认右边

                                if (x > 0) //百度人体识别，中心点位置
                                {
                                    logger?.Info($"CutImage: Baidu body analysis result: {x}");
                                    if (x + w2 / 2 > w) //右边
                                        start_w = w - w2;
                                    else if (x - w2 / 2 < 0)//左边
                                        start_w = 0;
                                    else //居中
                                        start_w = (int)x - w2 / 2;
                                }
                                else
                                {
                                    logger?.Info($"CutImage: No body analysis result, using default right alignment");
                                }

                                try
                                {
                                    using (var image = SKImage.FromBitmap(bitmap))
                                    {
                                        if (image == null)
                                        {
                                            logger?.Warn($"CutImage: Failed to create SKImage from bitmap for URL: {url}");
                                            return null;
                                        }
                                        
                                        using (var subset = image.Subset(SKRectI.Create(start_w, 0, w2, h)))
                                        {
                                            if (subset == null)
                                            {
                                                logger?.Warn($"CutImage: Failed to create image subset for URL: {url}");
                                                return null;
                                            }
                                            
                                            using (var encodedData = subset.Encode(SKEncodedImageFormat.Jpeg, 90))
                                            {
                                                if (encodedData == null)
                                                {
                                                    logger?.Warn($"CutImage: Failed to encode image subset for URL: {url}");
                                                    return null;
                                                }
                                                
                                                logger?.Info($"CutImage: Successfully cut image {w}x{h} --> start_w: {start_w}, size: {encodedData.Size}");
                                                
                                                // 将编码数据复制到新的内存流中，避免引用已释放的资源
                                                var resultMs = new MemoryStream();
                                                try
                                                {
                                                    var stream = encodedData.AsStream();
                                                    stream.CopyTo(resultMs);
                                                    resultMs.Position = 0;
                                                    
                                return new HttpResponseInfo()
                                {
                                                        Content = resultMs,
                                                        ContentLength = resultMs.Length,
                                    ContentType = "image/jpeg",
                                    StatusCode = HttpStatusCode.OK,
                                };
                            }
                                                catch (Exception ex)
                                                {
                                                    logger?.Error($"CutImage: Error copying encoded data to memory stream: {ex.Message}");
                                                    resultMs?.Dispose();
                                                    return null;
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger?.Error($"CutImage: Error during image cutting process: {ex.Message}");
                                    return null;
                                }
                            }

                            logger?.Info($"CutImage: Image does not need cutting. {w}x{h}");
                            return null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"CutImage: Unexpected error during image processing for URL {url}: {ex.Message}");
                logger?.Error($"CutImage: Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// 获取人脸的中间位置，
        /// </summary>
        /// <param name="bytes">图片数据</param>
        /// <param name="url">图片地址</param>
        /// <returns></returns>
        private async Task<double> GetBaiduBodyAnalysisResult(byte[] bytes, string url)
        {
            var baidu = Plugin.Instance?.Configuration?.GetBodyAnalysisService(jsonSerializer);
            if (baidu == null)
                return 0;

            if (string.IsNullOrWhiteSpace(url) == false)
            {
                var p = Plugin.Instance.db.ImageFaceCenterPoints.FindById(url)?.point;
                if (p != null)
                    return p.Value;
            }
            try
            {
                var r = await baidu.BodyAnalysis(bytes);
                if (r?.person_info?.Any() != true)
                    return 0;

                //取面积最大的人
                var p = r.person_info.Where(o => o.location?.score >= 0.1).OrderByDescending(o => o.location?.width * o.location?.height).FirstOrDefault()
                    ?? r.person_info.FirstOrDefault();

                //人数大于15个，且有15个小于最大人脸，则直接用最右边的做封面。其实也可以考虑识别左边的条码，有条码直接取右边，但Emby中实现困难
                if (p != null && r.person_info.Where(o => o.location?.left < p.location.left).Count() > 15 && r.person_info.Where(o => o.location?.left > p.location.left).Count() < 10)
                    return Save(p.location.left * 2);

                //鼻子
                if (p.body_parts.nose?.x > 0)
                    return Save(p.body_parts.nose.x);
                //嘴巴
                if (p.body_parts.left_mouth_corner?.x > 0 && p.body_parts.right_mouth_corner.x > 0)
                    return Save((p.body_parts.left_mouth_corner.x + p.body_parts.right_mouth_corner.x) / 2);

                //头顶
                if (p.body_parts.top_head?.x > 0)
                    return Save(p.body_parts.top_head.x);
                //颈部
                if (p.body_parts.neck?.x > 0)
                    return Save(p.body_parts.neck.x);
            }
            catch (Exception ex)
            {
                // 百度人体分析结果处理失败时记录日志
                logger?.Debug($"Failed to process Baidu body analysis result: {ex.Message}");
            }

            double Save(double d)
            {
                if (string.IsNullOrWhiteSpace(url) == false)
                {
                    var item = new ImageFaceCenterPoint() { url = url, point = d, created = DateTime.Now };
                    Plugin.Instance.db.ImageFaceCenterPoints.Upsert(item);
                }
                return d;
            }

            return 0;
        }

        private async Task<HttpResponseInfo> Parse(HttpResponseMessage resp)
        {
            try
            {
            var r = new HttpResponseInfo()
            {
                Content = await resp.Content.ReadAsStreamAsync(),
                ContentLength = resp.Content.Headers.ContentLength,
                ContentType = resp.Content.Headers.ContentType?.ToString(),
                StatusCode = resp.StatusCode,
                Headers = resp.Content.Headers.ToDictionary(o => o.Key, o => string.Join(", ", o.Value))
            };
            return r;
            }
            catch (Exception ex)
            {
                logger?.Error($"Parse: Error parsing HTTP response: {ex.Message}");
                return CreateEmptyResponse();
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
                    downloadSemaphore?.Dispose();
                    lastRequestTime?.Clear();
                }
                // 释放非托管资源
                // 如果HttpClientEx和SemaphoreSlim是托管资源，这里不需要额外释放
            }
            disposed = true;
        }
    }
}