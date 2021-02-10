using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.YoutubeMetadata.YTTools
{
    public static class YTUtils
    {
        public const string BaseUrl = "https://m.youtube.com/";
        //public const string YTID_RE = @"(?<=\[)[a-zA-Z0-9\-_]{11}(?=\])";
        // Simple Regex to capture everything between [] so it can be a channel, playlist or video
        public const string YTID_RE = @"[^[\]]+(?=])";
        /// <summary>
        ///  Returns the Youtube ID from the file path. Matches last 11 character field inside square brackets.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static string GetYTID(string name)
        {
            return Regex.Match(name, YTID_RE).Value;
        }

        public static bool IsChannel(string id)
        {
            if (id.StartsWith("UC")) return true;
            return false;
        }
    }
}
