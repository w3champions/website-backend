using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace W3ChampionsStatisticService.WebApi.ActionFilters
{
    public class W3CAuthenticationService : IW3CAuthenticationService
    {
        private static readonly string IdentificationApiUrl = Environment.GetEnvironmentVariable("IDENTIFICATION_API") ?? "https://identification-service.test.w3champions.com";

        public async Task<W3CUserAuthenticationDto> GetUserByToken(string bearer)
        {
            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(IdentificationApiUrl);
            var result = await httpClient.GetAsync($"/api/oauth/battleTag?bearer={bearer}");
            if (result.IsSuccessStatusCode)
            {
                var content = await result.Content.ReadAsStringAsync();
                var deserializeObject = JsonConvert.DeserializeObject<W3CUserAuthenticationDto>(content);
                return deserializeObject;
            }

            return null;
        }
    }

    public interface IW3CAuthenticationService
    {
        Task<W3CUserAuthenticationDto> GetUserByToken(string bearer);
    }
}