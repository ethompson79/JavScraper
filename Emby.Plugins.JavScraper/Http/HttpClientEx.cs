using System;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Plugins.JavScraper.Http
{
    /// <summary>
    /// HttpClient
    /// </summary>
    public class HttpClientEx : IDisposable
    {
        /// <summary>
        /// 客户端初始话方法
        /// </summary>
        private readonly Action<HttpClient> ac;

        /// <summary>
        /// 当前客户端
        /// </summary>
        private HttpClient client = null;

        /// <summary>
        /// 配置版本号
        /// </summary>
        private long version = -1;

        /// <summary>
        /// 上一个客户端
        /// </summary>
        private HttpClient client_old = null;
        
        /// <summary>
        /// 是否已释放资源
        /// </summary>
        private bool disposed = false;

        public HttpClientEx(Action<HttpClient> ac = null)
        {
            this.ac = ac;
        }

        /// <summary>
        /// 获取一个 HttpClient
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public HttpClient GetClient()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(HttpClientEx));
                
            if (client != null && version == Plugin.Instance.Configuration.ConfigurationVersion)
                return client;

            try
            {
                if (client_old != null)
                {
                    client_old.Dispose();
                    client_old = null;
                }
                client_old = client;

                var handler = new ProxyHttpClientHandler();
                client = new HttpClient(handler, true);
                ac?.Invoke(client);

                version = Plugin.Instance.Configuration.ConfigurationVersion;
                
                return client;
            }
            catch (Exception ex)
            {
                var logger = Plugin.Instance?.GetLogger("HttpClientEx");
                logger?.Error($"Failed to create HttpClient: {ex.Message}");
                
                // 如果创建失败，返回旧的客户端或创建基本客户端
                if (client_old != null)
                {
                    client = client_old;
                    client_old = null;
                    return client;
                }
                
                // 最后尝试创建基本HttpClient
                client = new HttpClient();
                ac?.Invoke(client);
                return client;
            }
        }

        public Task<string> GetStringAsync(string requestUri)
            => GetClient().GetStringAsync(requestUri);

        public Task<HttpResponseMessage> GetAsync(string requestUri)
            => GetClient().GetAsync(requestUri);

        public Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken cancellationToken)
            => GetClient().GetAsync(requestUri, cancellationToken);

        public Task<Stream> GetStreamAsync(string requestUri)
            => GetClient().GetStreamAsync(requestUri);

        public Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content)
            => GetClient().PostAsync(requestUri, content);

        public Uri BaseAddress => GetClient().BaseAddress;
        
        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// 释放资源的核心方法
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed && disposing)
            {
                try
                {
                    client?.Dispose();
                    client_old?.Dispose();
                }
                catch (Exception ex)
                {
                    var logger = Plugin.Instance?.GetLogger("HttpClientEx");
                    logger?.Warn($"Error disposing HttpClientEx: {ex.Message}");
                }
                finally
                {
                    client = null;
                    client_old = null;
                    disposed = true;
                }
            }
        }
        
        /// <summary>
        /// 析构函数
        /// </summary>
        ~HttpClientEx()
        {
            Dispose(false);
        }
    }
}