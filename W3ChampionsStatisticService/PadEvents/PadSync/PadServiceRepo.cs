using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;

namespace W3ChampionsStatisticService.PadEvents.PadSync
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

        public async Task<PlayerStatePad> GetPlayer(string battleTag)
        {
            var httpClient = new HttpClient();

            var encode = HttpUtility.UrlEncode(battleTag);
            var result = await httpClient.GetAsync($"https://api.w3champions.com/player/{encode}/stats");
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<PlayerStatePad>(content);
            return deserializeObject;
        }
    }

    public class PlayerStatePad
    {
        [JsonProperty("account")]
        public string Account { get; set; }

        [JsonProperty("data")]
        public Data Data { get; set; }
    }

    public class Data
    {
        [JsonProperty("stats")]
        public Stats Stats { get; set; }

        [JsonProperty("ladder")]
        public Dictionary<string, Ladder> Ladder { get; set; }
    }

    public class Ladder
    {
        [JsonProperty("wins", NullValueHandling = NullValueHandling.Ignore)]
        public long? Wins { get; set; }

        [JsonProperty("losses", NullValueHandling = NullValueHandling.Ignore)]
        public long? Losses { get; set; }

        [JsonProperty("solo", NullValueHandling = NullValueHandling.Ignore)]
        public Solo Solo { get; set; }
    }

    public class Solo
    {
        [JsonProperty("mmr")]
        public Mmr Mmr { get; set; }

        [JsonProperty("ranking")]
        public Ranking Ranking { get; set; }

        [JsonProperty("league")]
        public League League { get; set; }

        [JsonProperty("wins")]
        public long Wins { get; set; }

        [JsonProperty("losses")]
        public long Losses { get; set; }
    }

    public class League
    {
        [JsonProperty("order")]
        public long Order { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("division")]
        public long Division { get; set; }
    }

    public class Mmr
    {
        [JsonProperty("rating")]
        public double Rating { get; set; }

        [JsonProperty("rd")]
        public double Rd { get; set; }

        [JsonProperty("vol")]
        public double Vol { get; set; }
    }

    public class Ranking
    {
        [JsonProperty("progress")]
        public long Progress { get; set; }

        [JsonProperty("rp")]
        public double Rp { get; set; }

        [JsonProperty("lastGame")]
        public long LastGame { get; set; }

        [JsonProperty("rank")]
        public long Rank { get; set; }

        [JsonProperty("leagueId")]
        public long LeagueId { get; set; }

        [JsonProperty("leagueOrder")]
        public long LeagueOrder { get; set; }
    }

    public class Stats
    {
        [JsonProperty("human")]
        public Human Human { get; set; }

        [JsonProperty("orc")]
        public Human Orc { get; set; }

        [JsonProperty("undead")]
        public Human Undead { get; set; }

        [JsonProperty("night_elf")]
        public Human NightElf { get; set; }

        [JsonProperty("random")]
        public Human Random { get; set; }

        [JsonProperty("total")]
        public Human Total { get; set; }
    }

    public class Human
    {
        [JsonProperty("wins")]
        public long Wins { get; set; }

        [JsonProperty("losses")]
        public long Losses { get; set; }
    }


    public class MatchesList
    {
        public List<Match> items { get; set; }
        public long total { get; set; }
    }
}