using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;

using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using System.Text.Json;

namespace Jellyfin.Plugin.YoutubeMetadata.Providers
{
    public class CreatorProviderImageProvider : IRemoteImageProvider
    {
        private readonly IServerConfigurationManager _config;
        private readonly IFileSystem _fileSystem;
        private readonly IHttpClientFactory _httpClientFactory;
       
        public CreatorProviderImageProvider(IServerConfigurationManager config, IFileSystem fileSystem, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _fileSystem = fileSystem;
            _httpClientFactory = httpClientFactory;
        }

        public bool Supports(BaseItem item)
        {
            return item is Person;
        }

        public string Name => "YouTube Metadata";

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            yield return ImageType.Primary;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {

            var infos = new List<RemoteImageInfo>();
            var channelId = item.ProviderIds["youtubemetadata"];
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(channelId))
            {
                await EnsureInfo(channelId, cancellationToken).ConfigureAwait(false);

                string jsonString = File.ReadAllText(YoutubeEpisodeProvider.GetVideoInfoPath(_config.ApplicationPaths, channelId));
                var channel = JsonSerializer.Deserialize<Google.Apis.YouTube.v3.Data.Video>(jsonString);
                if (channel != null)
                {
                    var rii = new RemoteImageInfo();
                    if (channel.Snippet.Thumbnails.Maxres != null)
                    {
                        rii.Url = channel.Snippet.Thumbnails.Maxres.Url;
                    }
                    else if (channel.Snippet.Thumbnails.Standard != null)
                    {
                        rii.Url = channel.Snippet.Thumbnails.Standard.Url;
                    }
                    else if (channel.Snippet.Thumbnails.High != null)
                    {
                        rii.Url = channel.Snippet.Thumbnails.High.Url;
                    }
                    else if (channel.Snippet.Thumbnails.Medium != null)
                    {
                        rii.Url = channel.Snippet.Thumbnails.Medium.Url;
                    }
                    else if (channel.Snippet.Thumbnails.Default__.Url != null)
                    {
                        rii.Url = channel.Snippet.Thumbnails.Default__.Url;
                    }
                    infos.Add(rii);
                }
            }
            //return Task.FromResult<IEnumerable<RemoteImageInfo>>(infos);

            return infos;
        }
        internal Task EnsureInfo(string channelId, CancellationToken cancellationToken)
        {
            var ytPath = YoutubeEpisodeProvider.GetVideoInfoPath(_config.ApplicationPaths, channelId);

            var fileInfo = _fileSystem.GetFileSystemInfo(ytPath);

            if (fileInfo.Exists)
            {
                if ((DateTime.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileInfo)).TotalDays <= 10)
                {
                    return Task.CompletedTask;
                }
            }
            return DownloadInfo(channelId, cancellationToken);
        }
        /// <summary>
        /// Downloads metadata from Youtube API asyncronously and stores it as a json to cache.
        /// </summary>
        /// <param name="youtubeId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        internal async Task DownloadInfo(string channelId, CancellationToken cancellationToken)
        {
            await Task.Delay(10000).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = Plugin.Instance.Configuration.apikey,
                ApplicationName = this.GetType().ToString()
            });
            var vreq = youtubeService.Channels.List("snippet");
            vreq.Id = channelId;
            var response = await vreq.ExecuteAsync();
            var path = YoutubeEpisodeProvider.GetVideoInfoPath(_config.ApplicationPaths, channelId);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string jsonString = JsonSerializer.Serialize(response.Items[0]);
            File.WriteAllText(path, jsonString);
        }
        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient();
            return await httpClient.GetAsync(url).ConfigureAwait(false);
        }
    }
}
