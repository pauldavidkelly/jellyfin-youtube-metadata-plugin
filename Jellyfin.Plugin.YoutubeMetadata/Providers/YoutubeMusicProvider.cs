using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Entities;
using System.IO;
using System.Text.Json;

namespace Jellyfin.Plugin.YoutubeMetadata.Providers
{
    public class YoutubeMusicProvider : IRemoteMetadataProvider<MusicVideo, MusicVideoInfo>, IHasOrder
    {
        private readonly IServerConfigurationManager _config;
        private readonly IFileSystem _fileSystem;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<YoutubeMusicProvider> _logger;
        private readonly ILibraryManager _libmanager;

        public static YoutubeEpisodeProvider Current;

        public const string BaseUrl = "https://m.youtube.com/";
        public const string YTID_RE = @"(?<=\[)[a-zA-Z0-9\-_]{11}(?=\])";

        public YoutubeMusicProvider(IServerConfigurationManager config, IFileSystem fileSystem, IHttpClientFactory httpClientFactory, ILogger<YoutubeMusicProvider> logger, ILibraryManager libmanager)
        {
            _config = config;
            _fileSystem = fileSystem;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _libmanager = libmanager;
        }

        /// <inheritdoc />
        public string Name => "YouTube Metadata";

        /// <inheritdoc />
        public int Order => 1;

        public async Task<MetadataResult<MusicVideo>> GetMetadata(MusicVideoInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<MusicVideo>();
            var id = YoutubeEpisodeProvider.Current.GetYTID(info.Name);

            _logger.LogInformation(id);

            if (!string.IsNullOrWhiteSpace(id))
            {
                await YoutubeEpisodeProvider.Current.EnsureInfo(id, cancellationToken).ConfigureAwait(false);

                string jsonString = File.ReadAllText(YoutubeEpisodeProvider.GetVideoInfoPath(_config.ApplicationPaths, id));
                var video = JsonSerializer.Deserialize<Google.Apis.YouTube.v3.Data.Video>(jsonString);
                if (video != null)
                {
                    result.Item = new MusicVideo();
                    result.HasMetadata = true;
                    result.Item.OriginalTitle = info.Name;
                    YoutubeEpisodeProvider.Current.ProcessResult(result.Item, video);
                    result.AddPerson(YoutubeEpisodeProvider.CreatePerson(video.Snippet.ChannelTitle, video.Snippet.ChannelId));
                }
            }
            else
            {
                _logger.LogInformation("Youtube ID not found in filename of title: " + info.Name);
            }

            return result;
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MusicVideoInfo searchInfo, CancellationToken cancellationToken)
            => Task.FromResult(Enumerable.Empty<RemoteSearchResult>());

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }

}
