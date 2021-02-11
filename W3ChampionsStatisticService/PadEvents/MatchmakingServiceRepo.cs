using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.Admin;

namespace W3ChampionsStatisticService.PadEvents
{
    public class MatchmakingServiceRepo
    {
        private static readonly string MatchmakingApiUrl = Environment.GetEnvironmentVariable("MATCHMAKING_API") ?? "https://matchmaking-service.test.w3champions.com";
        private static readonly string MatchmakingAdminSecret = Environment.GetEnvironmentVariable("ADMIN_SECRET") ?? "300C018C-6321-4BAB-B289-9CB3DB760CBB";

        public async Task<BannedPlayerResponse> GetBannedPlayers()
        {
            var httpClient = new HttpClient();
            var result = await httpClient.GetAsync($"{MatchmakingApiUrl}/admin/bannedPlayers?secret={MatchmakingAdminSecret}");
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<BannedPlayerResponse>(content);
            return deserializeObject;
        }

        public async Task<HttpStatusCode> PostBannedPlayer(BannedPlayerReadmodel bannedPlayerReadmodel)
        {
            var httpClient = new HttpClient();
            var encodedTag = HttpUtility.UrlEncode(bannedPlayerReadmodel.battleTag);
            var httpcontent = new StringContent(JsonConvert.SerializeObject(bannedPlayerReadmodel), Encoding.UTF8, "application/json");
            var result = await httpClient.PostAsync($"{MatchmakingApiUrl}/admin/bannedPlayers/{encodedTag}?secret={MatchmakingAdminSecret}", httpcontent);
            return result.StatusCode;
        }

        public async Task<HttpStatusCode> DeleteBannedPlayer(BannedPlayerReadmodel bannedPlayerReadmodel)
        {
            var httpClient = new HttpClient();
            var encodedTag = HttpUtility.UrlEncode(bannedPlayerReadmodel.battleTag);
            var result = await httpClient.DeleteAsync($"{MatchmakingApiUrl}/admin/bannedPlayers/{encodedTag}?secret={MatchmakingAdminSecret}");
            return result.StatusCode;
        }

        public async Task<List<FormattedQueue>> GetLiveQueueData()
        {
            var httpClient = new HttpClient();
            var result = await httpClient.GetAsync($"{MatchmakingApiUrl}/queue/snapshots?secret={MatchmakingAdminSecret}");
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<List<Queue>>(content);
            return formatQueueData(deserializeObject); // formatted for easy use on frontend
        }
        
        private List<FormattedQueue> formatQueueData(List<Queue> allQueues)
        {
            try 
            {
                var formattedAllQueueData = new List<FormattedQueue>();
                if (allQueues != null) {
                    foreach (var queue in allQueues)
                    {
                        var gameModeQueue = new FormattedQueue();
                        gameModeQueue.gameMode = queue.gameMode;
                        
                        var formattedSingleQueueData = new List<FormattedPlayerData>();

                        if (queue.snapshot.Count > 0) {
                            foreach (var playerData in queue.snapshot)
                            {
                                var formattedPlayerData = new FormattedPlayerData();

                                // if it's an AT, data is taken from only 1 player
                                formattedPlayerData.battleTag = playerData.playerData[0].battleTag;
                                formattedPlayerData.mmr = playerData.mmr;
                                formattedPlayerData.quantile = playerData.quantiles.quantile;
                                formattedPlayerData.activityQuantile = playerData.quantiles.activityQuantile;
                                formattedPlayerData.queueTime = playerData.queueTime;
                                formattedPlayerData.isFloConnected = playerData.isFloConnected;
                                formattedPlayerData.location = playerData.playerData[0].location;
                                formattedPlayerData.serverOption = playerData.playerData[0].serverOption;
                                
                                formattedSingleQueueData.Add(formattedPlayerData);
                            }
                        }
                        gameModeQueue.snapshot = formattedSingleQueueData;

                        formattedAllQueueData.Add(gameModeQueue);
                    }
                }
            
                return formattedAllQueueData;
            }
            catch
            {
                return new List<FormattedQueue>();
            }
            
        }
    }

    public class FormattedQueue
    {
        public int gameMode { get; set; }
        public List<FormattedPlayerData> snapshot { get; set; }
    }

    public class FormattedPlayerData
    {
        public string battleTag { get; set; }
        public float mmr { get; set; }
        public float quantile { get; set; }
        public float activityQuantile { get; set; }
        public int queueTime { get; set; }
        public bool isFloConnected { get; set; }
        public string location { get; set; }
        public string serverOption { get; set; }
    }

    public class BannedPlayerResponse
    {
        public int total { get; set; }
        public List<BannedPlayerReadmodel> players { get; set; }
    }

    public class BannedPlayerReadmodel : IIdentifiable
    {
        public string battleTag { get; set; }

        public string endDate { get; set; }

        public bool isIpBan { get; set; }
        public bool? isOnlyChatBan { get; set; }

        public string banReason { get; set; }
        public string Id => battleTag;
    }
}