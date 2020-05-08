using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PadEvents.PadSync
{
    public interface IPadServiceRepo
    {
        Task<List<Match>> GetFrom(long offset);
        Task<PlayerStatePad> GetPlayer(string battleTag);
    }

    public class PadServiceRepo : IPadServiceRepo
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

        public async Task<List<League>> GetLeague(GateWay gateWay, GameMode gameMode)
        {
            var httpClient = new HttpClient();
            var result = await httpClient.GetAsync($"https://api.w3champions.com/leagues/{(int) gateWay}/{(int) gameMode}");
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<List<League>>(content);
            return deserializeObject;
        }
    }

    public class PlayerStatePad
    {
        public string account { get; set; }
        public Data data { get; set; }
    }

    public class Data
    {
        public Stats stats { get; set; }
        public Dictionary<string, PadLadder> ladder { get; set; }
    }

    public class PadLadder
    {
        public WinsAndLossesPad solo { get; set; }
    }

    public class Stats
    {
        public WinsAndLossesPad human { get; set; }
        public WinsAndLossesPad orc { get; set; }
        public WinsAndLossesPad undead { get; set; }
        public WinsAndLossesPad night_elf { get; set; }
        public WinsAndLossesPad random { get; set; }
    }

    public class WinsAndLossesPad
    {
        public int wins { get; set; }
        public int losses { get; set; }
    }

    public class MatchesList
    {
        public List<Match> items { get; set; }
        public long total { get; set; }
    }
}