
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Jellyfin.Plugin.YoutubeMetadata.YTTools;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.YoutubeMetadata
{
    public class YoutubeSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
    {
        private readonly IServerConfigurationManager _config;
        private readonly IFileSystem _fileSystem;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<YoutubeSeriesProvider> _logger;
        private readonly ILibraryManager _libmanager;
        private bool _isChannel;

        public string Name => "Youtube Metadata";
        public int Order => 1;

        public const string BaseUrl = "https://m.youtube.com/";
        public const string YTID_RE = @"(?<=\[)[a-zA-Z0-9\-_]{11}(?=\])";

        public static YoutubeSeriesProvider Current;

        public YoutubeSeriesProvider(IServerConfigurationManager config, IFileSystem fileSystem, IHttpClientFactory httpClientFactory, ILogger<YoutubeSeriesProvider> logger, ILibraryManager libmanager)
        {
            _config = config;
            _fileSystem = fileSystem;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _libmanager = libmanager;
            _isChannel = true;
            Current = this;
        }
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private string GetPathByTitle(string title)
        {
            var query = new MediaBrowser.Controller.Entities.InternalItemsQuery { Name = title };
            var results = _libmanager.GetItemsResult(query);
            return results.Items[0].Path;
        }

        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();
            var id = YTUtils.GetYTID(GetPathByTitle(info.Name));
            _isChannel = YTUtils.IsChannel(id);

            _logger.LogInformation(id);

            if (!string.IsNullOrWhiteSpace(id))
            {
                await EnsureInfo(id, cancellationToken).ConfigureAwait(false);
                string jsonString = File.ReadAllText(GetVideoInfoPath(_config.ApplicationPaths, id));
                var channel = JsonSerializer.Deserialize<Google.Apis.YouTube.v3.Data.Channel>(jsonString);
                
                if (channel != null)
                {
                    result.Item = new Series();
                    result.HasMetadata = true;
                    result.Item.OriginalTitle = info.Name;
                    ProcessResult(result.Item, channel);
                    result.AddPerson(CreatePerson(channel.Snippet.Title, channel.Id));
                }
            }
            else
            {
                _logger.LogInformation("Youtube ID not found in filename of title: " + info.Name);
            }

            return result;
        }
        /// <summary>
        /// Creates a person object of type director for the provided name.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="channel_id"></param>
        /// <returns></returns>
        public static PersonInfo CreatePerson(string name, string channel_id)
        {
            return new PersonInfo
            {
                Name = name,
                Type = PersonType.Director,
                ProviderIds = new Dictionary<string, string> { { "youtubemetadata", channel_id }
            },
            };
        }


        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
           => Task.FromResult(Enumerable.Empty<RemoteSearchResult>());


        /// <summary>
        /// Checks and returns data in local cache, downloads and returns if not present.
        /// </summary>
        /// <param name="youtubeID"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        internal Task EnsureInfo(string youtubeID, CancellationToken cancellationToken)
        {
            var ytPath = GetVideoInfoPath(_config.ApplicationPaths, youtubeID);

            var fileInfo = _fileSystem.GetFileSystemInfo(ytPath);

            if (fileInfo.Exists)
            {
                if ((DateTime.UtcNow - _fileSystem.GetLastWriteTimeUtc(fileInfo)).TotalDays <= 10)
                {
                    return Task.CompletedTask;
                }
            }
            return DownloadInfo(youtubeID, cancellationToken);
        }

        /// <summary>
        /// Processes the found metadata into the Movie entity.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="result"></param>
        /// <param name="preferredLanguage"></param>
        public void ProcessResult(Series item, Google.Apis.YouTube.v3.Data.Channel result)
        {
            item.Name = result.Snippet.Title;
            item.Overview = result.Snippet.Description;
            var date = DateTime.Parse(result.Snippet.PublishedAtRaw);
            item.ProductionYear = date.Year;
            item.PremiereDate = date;
        }

        

        /// <summary>
        /// Downloads metadata from Youtube API asyncronously and stores it as a json to cache.
        /// </summary>
        /// <param name="youtubeId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        internal async Task DownloadInfo(string youtubeId, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Downloading Remote Youtube");
            await Task.Delay(10000).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = Plugin.Instance.Configuration.apikey,
                ApplicationName = this.GetType().ToString()
            });
            if (_isChannel)
                await DownloadChannel(youtubeId, youtubeService);
            else
                await DownloadPlaylist(youtubeId, youtubeService);

        }

        internal async Task DownloadChannel(string youtubeId, YouTubeService youtubeService)
        {
           var vreq = youtubeService.Channels.List("snippet");
           vreq.Id = youtubeId;
           var response = await vreq.ExecuteAsync();
           var path = GetVideoInfoPath(_config.ApplicationPaths, youtubeId);
           Directory.CreateDirectory(Path.GetDirectoryName(path));
           string jsonString = JsonSerializer.Serialize(response.Items[0]);
           File.WriteAllText(path, jsonString);
        }

        internal async Task DownloadPlaylist(string youtubeId, YouTubeService youtubeService)
        {
            var vreq = youtubeService.Playlists.List("snippet");
            vreq.Id = youtubeId;
            var response = await vreq.ExecuteAsync();
            var path = GetVideoInfoPath(_config.ApplicationPaths, youtubeId);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string jsonString = JsonSerializer.Serialize(response.Items[0]);
            File.WriteAllText(path, jsonString);
        }

        /// <summary>
        /// Gets the data path of a video provided the youtube ID.
        /// </summary>
        /// <param name="appPaths"></param>
        /// <param name="youtubeId"></param>
        /// <returns></returns>
        private static string GetVideoDataPath(IApplicationPaths appPaths, string youtubeId)
        {
            var dataPath = Path.Combine(GetVideoDataPath(appPaths), youtubeId);

            return dataPath;
        }

        /// <summary>
        /// Gets the Youtube Metadata root cache path.
        /// </summary>
        /// <param name="appPaths"></param>
        /// <returns></returns>
        private static string GetVideoDataPath(IApplicationPaths appPaths)
        {
            var dataPath = Path.Combine(appPaths.CachePath, "youtubemetadata");

            return dataPath;
        }

        /// <summary>
        /// Gets the path to information on a specific video in the cache.
        /// </summary>
        /// <param name="appPaths"></param>
        /// <param name="youtubeID"></param>
        /// <returns></returns>
        internal static string GetVideoInfoPath(IApplicationPaths appPaths, string youtubeID)
        {
            var dataPath = GetVideoDataPath(appPaths, youtubeID);

            return Path.Combine(dataPath, "ytvideo.json");
        }
    }
}
