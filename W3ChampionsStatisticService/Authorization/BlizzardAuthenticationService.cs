using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Authorization
{
    public class BlizzardAuthenticationService : IBlizzardAuthenticationService
    {
        private readonly string _bnetClientId = Environment.GetEnvironmentVariable("BNET_API_CLIENT_ID") ?? "6da84149578f4566a4d0224a2264a54d";
        private readonly string _bnetApiSecret = Environment.GetEnvironmentVariable("BNET_API_SECRET") ?? "m112C7BJql48WOSLUPmWLbND4ZvhwN1V";

        public async Task<BlizzardUserInfo> GetUser(string bearer)
        {
            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("https://eu.battle.net/oauth/userinfo");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

            var res = await httpClient.GetAsync("");
            if (!res.IsSuccessStatusCode)
            {
                return null;
            }

            var readAsStringAsync = await res.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<BlizzardUserInfo>(readAsStringAsync);
        }

        public async Task<OAuthToken> GetToken(string code, string redirectUri)
        {
            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("https://eu.battle.net/oauth/token");
            var res = await httpClient.GetAsync(
                $"?region=eu&code={code}&grant_type=authorization_code&redirect_uri={redirectUri}&client_id={_bnetClientId}&client_secret={_bnetApiSecret}");
            if (!res.IsSuccessStatusCode)
            {
                return null;
            }

            var readAsStringAsync = await res.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<OAuthToken>(readAsStringAsync);
        }
    }
}