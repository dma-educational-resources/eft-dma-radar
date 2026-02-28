using eft_dma_radar.Common.Misc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace eft_dma_radar.UI.Misc
{
    /// <summary>
    /// Static utility methods for streaming status checking
    /// </summary>
    public static class StreamingUtils
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Check if a streamer is currently live based on their platform and username
        /// </summary>
        /// <param name="platform">The streaming platform</param>
        /// <param name="username">The username on the platform</param>
        /// <returns>True if the streamer is live, false otherwise</returns>
        public static async Task<bool> IsLive(StreamingPlatform platform, string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            try
            {
                if (username.StartsWith("http"))
                {
                    if (username.Contains("twitch.tv"))
                        return await CheckTwitchLive(username);
                    else if (username.Contains("youtube.com"))
                        return await CheckYouTubeLive(username);
                    else
                        return false;
                }

                switch (platform)
                {
                    case StreamingPlatform.Twitch:
                        return await CheckTwitchLive(username);
                    case StreamingPlatform.YouTube:
                        return await CheckYouTubeLive(username);
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Streaming] Error checking if {username} is live on {platform}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if a Twitch streamer is live
        /// </summary>
        private static async Task<bool> CheckTwitchLive(string input)
        {
            try
            {
                string url;
                if (input.StartsWith("http"))
                {
                    url = input;
                }
                else if (input.Contains("twitch.tv/"))
                {
                    var parts = input.Split(new[] { "twitch.tv/" }, StringSplitOptions.RemoveEmptyEntries);
                    url = $"https://twitch.tv/{parts[parts.Length - 1].Split('/')[0]}";
                }
                else
                {
                    url = $"https://twitch.tv/{input}";
                }

                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        XMLogging.WriteLine($"[Streaming] Failed to fetch {url}: Status code {response.StatusCode}");
                        return false;
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    return content.Contains("isLiveBroadcast");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Streaming] Twitch check error for {input}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if a YouTube streamer is live
        /// </summary>
        private static async Task<bool> CheckYouTubeLive(string input)
        {
            try
            {
                string url;
                if (input.StartsWith("http"))
                {
                    url = input;
                    if (!url.Contains("/live"))
                        url += "/live";
                }
                else if (input.Contains("youtube.com/@"))
                {
                    var parts = input.Split(new[] { "youtube.com/@" }, StringSplitOptions.RemoveEmptyEntries);
                    url = $"https://youtube.com/@{parts[parts.Length - 1].Split('/')[0]}/live";
                }
                else
                {
                    url = $"https://youtube.com/@{input}/live";
                }

                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        XMLogging.WriteLine($"[Streaming] Failed to fetch {url}: Status code {response.StatusCode}");
                        return false;
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    return content.Contains("\"is_viewed_live\",\"value\":\"True\"");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Streaming] YouTube check error for {input}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get a formatted streaming URL for the given platform and username
        /// </summary>
        /// <param name="platform">The streaming platform</param>
        /// <param name="username">The username on the platform</param>
        /// <returns>Formatted URL string</returns>
        public static string GetStreamingURL(StreamingPlatform platform, string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return string.Empty;

            if (username.StartsWith("http"))
                return username;

            switch (platform)
            {
                case StreamingPlatform.Twitch:
                    return $"https://twitch.tv/{username}";

                case StreamingPlatform.YouTube:
                    return $"https://youtube.com/@{username}/live";

                default:
                    return string.Empty;
            }
        }
    }
}
