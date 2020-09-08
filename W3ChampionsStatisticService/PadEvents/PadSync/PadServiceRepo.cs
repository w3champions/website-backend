using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PadEvents.PadSync
{
    public class PadServiceRepo
    {
        private static string MatchmakingApiUrl = Environment.GetEnvironmentVariable("MATCHMAKING_API") ?? "https://matchmaking-service.test.w3champions.com";
        private static string MatchmakingAdminSecret = Environment.GetEnvironmentVariable("ADMIN_SECRET") ?? "300C018C-6321-4BAB-B289-9CB3DB760CBB";

        public async Task<List<Match>> GetFrom(long offset)
        {
            var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(MatchmakingApiUrl);
            var result = await httpClient.GetAsync($"/match?limit=100&offset={offset}");
            var content = await result.Content.ReadAsStringAsync();
            var deserializeObject = JsonConvert.DeserializeObject<MatchesList>(content);
            return deserializeObject.items;
        }

        public async Task<PlayerStatePad> GetPlayer(string battleTag)
        {
            var httpClient = new HttpClient();
            var encode = HttpUtility.UrlEncode(battleTag);
            var result = await httpClient.GetAsync($"{MatchmakingApiUrl}/player/{encode}/stats");
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<PlayerStatePad>(content);
            return deserializeObject;
        }

        public async Task<BannedPlayerResponse> GetBannedPlayers()
        {
            var httpClient = new HttpClient();
            var result = await httpClient.GetAsync($"{MatchmakingApiUrl}/admin/bannedPlayers?secret={MatchmakingAdminSecret}");
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<BannedPlayerResponse>(content);
            return deserializeObject;
        }

        public async Task<System.Net.HttpStatusCode> PostBannedPlayers(BannedPlayer bannedPlayer)
        {
            var httpClient = new HttpClient();
            var encodedTag = HttpUtility.UrlEncode(bannedPlayer.battleTag);
            var httpcontent = new StringContent(JsonConvert.SerializeObject(bannedPlayer), Encoding.UTF8, "application/json");
            var result = await httpClient.PostAsync($"{MatchmakingApiUrl}/admin/bannedPlayers/{encodedTag}?secret={MatchmakingAdminSecret}", httpcontent);
            return result.StatusCode;
        }

        public async Task<System.Net.HttpStatusCode> DeleteBannedPlayers(BannedPlayer bannedPlayer)
        {
            var httpClient = new HttpClient();
            var encodedTag = HttpUtility.UrlEncode(bannedPlayer.battleTag);
            var result = await httpClient.DeleteAsync($"{MatchmakingApiUrl}/admin/bannedPlayers/{encodedTag}?secret={MatchmakingAdminSecret}");
            return result.StatusCode;
        }

        public async Task<LeagueConstellation> GetLeague(GateWay gateWay, GameMode gameMode)
        {
            var httpClient = new HttpClient();
            var result = await httpClient.GetAsync($"{MatchmakingApiUrl}/leagues/{(int)gateWay}/{(int)gameMode}");
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

    public class BannedPlayer : IIdentifiable
    {
        public string battleTag { get; set; }

        public string endDate { get; set; }

        public bool isIpBan { get; set; }
        public bool? isOnlyChatBan { get; set; }

        public string banReason { get; set; }
        public string Id => battleTag;
    }
}