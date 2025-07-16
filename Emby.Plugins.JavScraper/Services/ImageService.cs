using System.Threading.Tasks;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Logging;
using System;

namespace Emby.Plugins.JavScraper.Services
{
    /// <summary>
    /// 转发图片信息 - 不需要认证的公共服务
    /// </summary>
    [Route("/emby/Plugins/JavScraper/Image", "GET", Summary = "Gets image proxy without authentication")]
    [Route("/Plugins/JavScraper/Image", "GET", Summary = "Gets image proxy without authentication")]
    public class GetImageInfo
    {
        /// <summary>
        /// 图像类型
        /// </summary>
        public ImageType? type { get; set; }

        /// <summary>
        /// 地址
        /// </summary>
        public string url { get; set; }
    }

    /// <summary>
    /// 图片代理服务 - 允许匿名访问以支持内部调用
    /// </summary>
    public class ImageService : IService, IRequiresRequest
    {
        private readonly ImageProxyService imageProxyService;
        private readonly IHttpResultFactory resultFactory;
        private readonly ILogger logger;

        /// <summary>
        /// Gets or sets the request context.
        /// </summary>
        /// <value>The request context.</value>
        public IRequest Request { get; set; }

        public ImageService(ILogManager logManager, IHttpResultFactory resultFactory)
        {
            imageProxyService = Plugin.Instance?.ImageProxyService;
            this.resultFactory = resultFactory;
            this.logger = logManager.CreateLogger<ImageService>();
        }

        /// <summary>
        /// 处理GET请求 - 允许匿名访问
        /// </summary>
        public async Task<object> Get(GetImageInfo request)
        {
            try
            {
                // 检查服务是否可用
                if (imageProxyService == null)
                {
                    logger?.Error("ImageService: ImageProxyService is not available");
                    return CreateEmptyResult();
                }
                
                return await DoGet(request?.url, request?.type);
            }
            catch (Exception ex)
            {
                logger?.Error($"ImageService: Error in Get method: {ex.Message}");
                return CreateEmptyResult();
            }
        }

        /// <summary>
        /// 转发信息
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private async Task<object> DoGet(string url, ImageType? type)
        {
            try
            {
                logger?.Info($"ImageService: Processing request for URL: {url}, Type: {type}");
                logger?.Info($"ImageService: Request headers: {Request?.UserAgent ?? "Unknown"}");

                if (string.IsNullOrWhiteSpace(url))
                {
                    logger?.Error("ImageService: URL is null or empty");
                    return resultFactory.GetResult(Request, new System.IO.MemoryStream(), "text/plain");
                }

            if (url.IsWebUrl() != true)
                {
                    logger?.Error($"ImageService: Invalid URL format: {url}");
                    return resultFactory.GetResult(Request, new System.IO.MemoryStream(), "text/plain");
                }

                logger?.Info($"ImageService: Calling imageProxyService.GetImageResponse for URL: {url}");
            var resp = await imageProxyService.GetImageResponse(url, type ?? ImageType.Backdrop, default);
                
                if (resp == null)
                {
                    logger?.Error($"ImageService: No response received for URL: {url}");
                    return resultFactory.GetResult(Request, new System.IO.MemoryStream(), "text/plain");
                }

                if (resp.Content == null)
                {
                    logger?.Error($"ImageService: Response content is null for URL: {url}");
                    return resultFactory.GetResult(Request, new System.IO.MemoryStream(), "text/plain");
                }

                if (!(resp.ContentLength > 0))
                {
                    logger?.Warn($"ImageService: Empty or unknown content length for URL: {url}, ContentLength: {resp.ContentLength}");
                    // 不立即返回错误，尝试处理内容
                }

                logger?.Info($"ImageService: Successfully processed URL: {url}, Content-Length: {resp.ContentLength}, Content-Type: {resp.ContentType}");
                
                // 确保内容类型有默认值
                var contentType = resp.ContentType ?? "image/jpeg";
                
                return resultFactory.GetResult(Request, resp.Content, contentType);
            }
            catch (Exception ex)
            {
                logger?.Error($"ImageService: Error processing URL {url}: {ex.Message}");
                logger?.Error($"ImageService: Stack trace: {ex.StackTrace}");
                
                // 返回空响应而不是抛出异常，避免500错误
                try
                {
                    return resultFactory.GetResult(Request, new System.IO.MemoryStream(), "text/plain");
                }
                catch (Exception ex2)
                {
                    logger?.Error($"ImageService: Error creating error response: {ex2.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// 创建空结果以避免异常
        /// </summary>
        private object CreateEmptyResult()
        {
            try
            {
                return resultFactory.GetResult(Request, new System.IO.MemoryStream(), "image/jpeg");
            }
            catch
            {
                // 如果连创建空结果都失败，返回null让Emby处理
                return null;
            }
        }
    }
}