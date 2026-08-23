using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources
{
    public static class UpdateChecker
    {


        public static async Task<string?> GetLatestVersionAsync()
        {
            try
            {
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(10)
                };


                client.DefaultRequestHeaders.UserAgent.ParseAdd("FramesToMMSpriteResources");

                const string url = "https://api.github.com/repos/Marci599/sprite-rips-to-mm-sprite-resources/releases/latest";

                using var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return null;


                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;


                return root.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() : null;
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (TaskCanceledException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }

        }
    }
}
