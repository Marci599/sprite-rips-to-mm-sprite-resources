using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace FramesToMMSpriteResource
{
    public static class UpdateChecker
    {


        public static async Task<string?> GetLatestVersionAsync()
        {
            using var client = new HttpClient();

            // GitHub API megköveteli a User-Agent headert
            client.DefaultRequestHeaders.Add("User-Agent", "request");

            var url = $"https://api.github.com/repos/Marci599/sprite-rips-to-mm-sprite-resources/releases/latest";

            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return root.GetProperty("tag_name").GetString();
        }
    }
}
