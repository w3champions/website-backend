using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Authorization
{
    public class TwitchAuthenticationService : ITwitchAuthenticationService
    {
        private OAuthToken _cachedToken { get; set; }

        private readonly string _twitchApiSecret = Environment.GetEnvironmentVariable("TWITCH_API_SECRET");

        public async Task<OAuthToken> GetToken()
        {
            // Twitch token expires after 60 days, so this cache will save many calls to the twitch API
            if (Cache.TwitchToken != null && !Cache.TwitchToken.hasExpired())
            {
                return Cache.TwitchToken;
            }

            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("https://id.twitch.tv/oauth2/token");
            var result = await httpClient.PostAsync($"?client_id=38ac0gifyt5khcuq23h2p8zpcqosbc&client_secret={_twitchApiSecret}&grant_type=client_credentials", null);
            if (result.StatusCode == HttpStatusCode.OK)
            {
                var content = await result.Content.ReadAsStringAsync();
                _cachedToken = JsonConvert.DeserializeObject<OAuthToken>(content);
                _cachedToken.CreateDate = DateTime.Now;
                Cache.TwitchToken = _cachedToken;
                return _cachedToken;
            }

            throw new Exception("Could not retrieve Twitch Token");
        }

    }

    public static class Cache
    {
        public static OAuthToken TwitchToken { get; set; }
    }
}