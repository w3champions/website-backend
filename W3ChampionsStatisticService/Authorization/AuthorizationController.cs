using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace W3ChampionsStatisticService.Authorization
{

    [ApiController]
    [Route("api/oauth")]
    public class AuthorizationController : ControllerBase
    {
        [HttpGet("token")]
        public async Task<IActionResult> ResetAllReadModels([FromQuery] string code, [FromQuery] string redirectUri)
        {
            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("https://eu.battle.net/oauth/token");
            var res = await httpClient.GetAsync(
                $"?region=eu&code={code}&grant_type=authorization_code&redirect_uri={redirectUri}&client_id=d7bd6dd46e2842c8a680866759ad34c2&client_secret=7qs8iu2dcX4ZrURpIpezwZJHNM7OJmXg");
            if (!res.IsSuccessStatusCode)
            {
                return Unauthorized("Sorry H4ckerb0i");
            }

            var readAsStringAsync = await res.Content.ReadAsStringAsync();
            var token = JsonConvert.DeserializeObject<BlizzardToken>(readAsStringAsync);
            return Ok(token);
        }


        [HttpGet("battleTag")]
        public async Task<IActionResult> ResetAllReadModels([FromQuery] string bearer)
        {
            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("https://eu.battle.net/oauth/userinfo");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

            var res = await httpClient.GetAsync("");
            if (!res.IsSuccessStatusCode)
            {
                return Unauthorized("Sorry H4ckerb0i");
            }

            var readAsStringAsync = await res.Content.ReadAsStringAsync();
            var userInfo = JsonConvert.DeserializeObject<BlizzardUserInfo>(readAsStringAsync);
            return Ok(userInfo);
        }
    }
}