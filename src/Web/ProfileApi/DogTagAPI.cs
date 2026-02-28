using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using eft_dma_radar.Common.Misc;
namespace eft_dma_radar.Tarkov.API
{
    internal static class DogtagApiClient
    {
        private const string ApiKey = "eft_7f8d2c4b9a1e3d6a1";
        private const string ApiUrl = "http://45.61.50.254:8000/dogtag";

        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2)
            };

            client.DefaultRequestHeaders.Add("X-API-Key", ApiKey);
            return client;
        }

        public static void Send(
            string accountId,
            string profileId,
            string nickname)
        {
            if (string.IsNullOrEmpty(accountId) ||
                string.IsNullOrEmpty(profileId) ||
                string.IsNullOrEmpty(nickname))
                return;

            var payload = new
            {
                accountId,
                profileId,
                nickname
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

            // Fire-and-forget, but observe result
            _ = SendInternalAsync(content, accountId, profileId);
        }

        private static async Task SendInternalAsync(
            HttpContent content,
            string accountId,
            string profileId)
        {
            try
            {
                using var response = await _http.PostAsync(ApiUrl, content)
                                                 .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    XMLogging.WriteLine(
                        $"[DogtagAPI] FAILED {response.StatusCode} " +
                        $"AccountId={accountId} ProfileId={profileId}");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine(
                    $"[DogtagAPI] EXCEPTION {ex.Message} " +
                    $"AccountId={accountId} ProfileId={profileId}");
            }
        }
    }
}