using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.Matches
{
    public class PadServiceRepo
    {
        public async Task<List<Match>> GetFrom(long offset)
        {
            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("https://api.w3champions.com");
            var result = await httpClient.GetAsync($"/match?limit=100&offset={offset}");
            var content = await result.Content.ReadAsStringAsync();
            var deserializeObject = JsonConvert.DeserializeObject<MatchesList>(content);
            return deserializeObject.items;
        }
    }

    public class MatchesList
    {
        public List<Match> items { get; set; }
        public long total { get; set; }
    }
}