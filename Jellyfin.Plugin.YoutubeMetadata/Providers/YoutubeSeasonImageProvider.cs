using Jellyfin.Data.Entities.Libraries;
using Jellyfin.Plugin.YoutubeMetadata.YTTools;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.YoutubeMetadata.Providers
{
    public class YoutubeSeasonImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly IServerConfigurationManager _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<YoutubeSeasonImageProvider> _logger;
        public static YoutubeSeasonProvider Current;

        public YoutubeSeasonImageProvider(IServerConfigurationManager config, IHttpClientFactory httpClientFactory, ILogger<YoutubeSeasonImageProvider> logger)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }
        public string Name => "Youtube Metadata";

        public int Order => 1;
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient();
            return httpClient.GetAsync(url);
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var id = YTUtils.GetYTID(item.FileNameWithoutExtension);

            if (!string.IsNullOrWhiteSpace(id))
            {
                await YoutubeSeasonProvider.Current.EnsureInfo(id, cancellationToken).ConfigureAwait(false);

                string jsonString = File.ReadAllText(YoutubeSeasonProvider.GetVideoInfoPath(_config.ApplicationPaths, id));
                var obj = JsonSerializer.Deserialize<Google.Apis.YouTube.v3.Data.Video>(jsonString);
                if (obj != null)
                {
                    var tnurls = new List<string>();
                    if (obj.Snippet.Thumbnails.Maxres != null)
                    {
                        tnurls.Add(obj.Snippet.Thumbnails.Maxres.Url);
                    }
                    if (obj.Snippet.Thumbnails.Standard != null)
                    {
                        tnurls.Add(obj.Snippet.Thumbnails.Standard.Url);
                    }
                    if (obj.Snippet.Thumbnails.High != null)
                    {
                        tnurls.Add(obj.Snippet.Thumbnails.High.Url);
                    }
                    if (obj.Snippet.Thumbnails.Medium != null)
                    {
                        tnurls.Add(obj.Snippet.Thumbnails.Medium.Url);
                    }
                    if (obj.Snippet.Thumbnails.Default__.Url != null)
                    {
                        tnurls.Add(obj.Snippet.Thumbnails.Default__.Url);
                    }

                    return GetImages(tnurls);
                }
                else
                {
                    _logger.LogInformation("Object is null!");
                }
            }

            return new List<RemoteImageInfo>();
        }

        private IEnumerable<RemoteImageInfo> GetImages(IEnumerable<string> urls)
        {
            var list = new List<RemoteImageInfo>();
            foreach (string url in urls)
            {
                if (!string.IsNullOrWhiteSpace(url))
                {
                    list.Add(new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Url = url,
                        Type = ImageType.Primary
                    });
                }
            }
            return list;
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new List<ImageType>
            {
                ImageType.Primary,
                ImageType.Disc
            };
        }

        /// <inheritdoc />
        public bool Supports(BaseItem item)
            => item is Season;
    }
}
