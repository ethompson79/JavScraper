using Emby.Plugins.JavScraper.Configuration;
using MihaZupan;
using System;
using System.Net;

namespace Emby.Plugins.JavScraper.Http
{
    /// <summary>
    /// 代理服务器
    /// </summary>
    public class JavWebProxy : IWebProxy
    {
        private IWebProxy proxy;

        /// <summary>
        /// 代理服务
        /// </summary>
        public IWebProxy Proxy
        {
            get => proxy ?? WebRequest.DefaultWebProxy;
            set => proxy = value;
        }

        /// <summary>
        /// The credentials to submit to the proxy server for authentication.
        /// </summary>
        public ICredentials Credentials
        {
            get => Proxy.Credentials;
            set => Proxy.Credentials = value;
        }

        public JavWebProxy()
        {
            Reset();
        }

        /// <summary>
        /// 重设代理
        /// </summary>
        public void Reset()
        {
            var old = Proxy;
            var options = Plugin.Instance.Configuration;
            switch ((ProxyTypeEnum)options.ProxyType)
            {
                case ProxyTypeEnum.None:
                case ProxyTypeEnum.JsProxy:
                default:
                    Proxy = null;
                    break;


            }
            if (old is HttpToSocks5Proxy s5)
                s5.StopInternalServer();
        }

        /// <summary>
        /// Returns the URI of a proxy.
        /// </summary>
        /// <param name="destination">A System.Uri that specifies the requested Internet resource.</param>
        /// <returns>A System.Uri instance that contains the URI of the proxy used to contact destination.</returns>
        public Uri GetProxy(Uri destination)
            => Proxy.GetProxy(destination);

        /// <summary>
        /// Indicates that the proxy should not be used for the specified host.
        /// </summary>
        /// <param name="host">The System.Uri of the host to check for proxy use.</param>
        /// <returns></returns>
        public bool IsBypassed(Uri host)
        {
            var options = Plugin.Instance.Configuration;
            if (options.ProxyType == (int)ProxyTypeEnum.None || options.EnableJsProxy)
                return true;
            if (options.IsBypassed(host.Host))
                return true;

            return proxy.IsBypassed(host);
        }
    }
}