using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;

namespace W3ChampionsStatisticService.PadEvents.PadSync
{
    public interface IPadServiceRepo
    {
        Task<List<Match>> GetFrom(long offset);
        Task<PlayerStatePad> GetPlayer(string battleTag);
        Task<LeagueConstellation> GetLeague(GateWay gateWay, GameMode gameMode);
    }

    public class PadServiceRepo : IPadServiceRepo
    {
        public async Task<List<Match>> GetFrom(long offset)
        {
            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("http://api.w3champions.com:25059");
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

        public async Task<BannedPlayerResponse> GetBannedPlayers()
        {
            var httpClient = new HttpClient();
            var result = await httpClient.GetAsync($"https://api.w3champions.com/admin/bannedPlayers?secret=yGBC1w5TcjAQSyOvGTlO4kPGCdJ7dGBKNPkEKQVnqHonGLet4DntoA7PwCgHAiSJ");
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<BannedPlayerResponse>(content);
            return deserializeObject;
        }

        public async Task<System.Net.HttpStatusCode> PostBannedPlayers(BannedPlayer bannedPlayer)
        {
            var httpClient = new HttpClient();
            var splitTag = bannedPlayer.battleTag.Split('#');
            var httpcontent = new StringContent(JsonConvert.SerializeObject(bannedPlayer), Encoding.UTF8, "application/json");
            var result = await httpClient.PostAsync($"https://api.w3champions.com/admin/bannedPlayers/{splitTag[0] + "%23" + splitTag[1]}?secret=yGBC1w5TcjAQSyOvGTlO4kPGCdJ7dGBKNPkEKQVnqHonGLet4DntoA7PwCgHAiSJ", httpcontent);
            return result.StatusCode;
        }

        public async Task<System.Net.HttpStatusCode> DeleteBannedPlayers(BannedPlayer bannedPlayer)
        {
            var httpClient = new HttpClient();
            var splitTag = bannedPlayer.battleTag.Split('#');
            var result = await httpClient.DeleteAsync($"https://api.w3champions.com/admin/bannedPlayers/{splitTag[0] + "%23" + splitTag[1]}?secret=yGBC1w5TcjAQSyOvGTlO4kPGCdJ7dGBKNPkEKQVnqHonGLet4DntoA7PwCgHAiSJ");
            return result.StatusCode;
        }

        public async Task<LeagueConstellation> GetLeague(GateWay gateWay, GameMode gameMode)
        {
            var httpClient = new HttpClient();
            var result = await httpClient.GetAsync($"https://api.w3champions.com/leagues/{(int)gateWay}/{(int)gameMode}");
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<List<League>>(content);
            return new LeagueConstellation(0, gateWay, gameMode, deserializeObject);
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

    public class BannedPlayerResponse
    {
        public int total { get; set; }
        public List<BannedPlayer> players { get; set; }
    }

    public class BannedPlayer
    {
        public string battleTag { get; set; }

        public string endDate { get; set; }

        public Boolean isIpBan { get; set; }

        public string banReason { get; set; }
    }
}