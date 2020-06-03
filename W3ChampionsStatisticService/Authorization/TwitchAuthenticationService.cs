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

        public async Task<OAuthToken> GetToken(string clientId, string clientSecret)
        {
            if (_cachedToken != null && !_cachedToken.hasExpired())
            {
                return _cachedToken;
            }

            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("https://id.twitch.tv/oauth2/token");
            var result = await httpClient.PostAsync($"?client_id={clientId}&client_secret={clientSecret}&grant_type=client_credentials", null);
            if (result.StatusCode == HttpStatusCode.OK)
            {
                var content = await result.Content.ReadAsStringAsync();
                _cachedToken = JsonConvert.DeserializeObject<OAuthToken>(content);
                _cachedToken.CreateDate = DateTime.Now;
                return _cachedToken;
            }
            else
            {
                throw new Exception("Could not retrieve Twitch Token");
            }
        }
    }
}