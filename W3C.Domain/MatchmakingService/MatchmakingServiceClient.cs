using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Dynamic;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.MatchmakingContracts;
using W3C.Domain.MatchmakingService.MatchmakingContracts;
using W3C.Domain.MatchmakingService.MatchmakingContracts.Tournaments;
using W3C.Domain.Repositories;
using System.Net.Http.Json;

namespace W3C.Domain.MatchmakingService
{
    public class MatchmakingServiceClient
    {
        private static readonly string MatchmakingApiUrl = Environment.GetEnvironmentVariable("MATCHMAKING_API") ?? "https://matchmaking-service.test.w3champions.com";
        private static readonly string AdminSecret = Environment.GetEnvironmentVariable("ADMIN_SECRET") ?? "300C018C-6321-4BAB-B289-9CB3DB760CBB";
        private readonly JsonSerializerSettings _jsonSerializerSettings;

        private readonly HttpClient _httpClient;
        public MatchmakingServiceClient(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient();

            _jsonSerializerSettings = new JsonSerializerSettings()
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                }
            };
        }

        public async Task<BannedPlayerResponse> GetBannedPlayers()
        {
            var result = await _httpClient.GetAsync($"{MatchmakingApiUrl}/admin/bannedPlayers?secret={AdminSecret}");
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<BannedPlayerResponse>(content);
            return deserializeObject;
        }

        public async Task<HttpStatusCode> PostBannedPlayer(BannedPlayerReadmodel bannedPlayerReadmodel)
        {
            var encodedTag = HttpUtility.UrlEncode(bannedPlayerReadmodel.battleTag);
            var httpcontent = new StringContent(JsonConvert.SerializeObject(bannedPlayerReadmodel), Encoding.UTF8, "application/json");
            var result = await _httpClient.PostAsync($"{MatchmakingApiUrl}/admin/bannedPlayers/{encodedTag}?secret={AdminSecret}", httpcontent);
            return result.StatusCode;
        }

        public async Task<HttpStatusCode> DeleteBannedPlayer(BannedPlayerReadmodel bannedPlayerReadmodel)
        {
            var encodedTag = HttpUtility.UrlEncode(bannedPlayerReadmodel.battleTag);
            var result = await _httpClient.DeleteAsync($"{MatchmakingApiUrl}/admin/bannedPlayers/{encodedTag}?secret={AdminSecret}");
            return result.StatusCode;
        }

        public async Task<List<FormattedQueue>> GetLiveQueueData()
        {
            var result = await _httpClient.GetAsync($"{MatchmakingApiUrl}/queue/snapshots?secret={AdminSecret}");
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<List<Queue>>(content);
            return FormatQueueData(deserializeObject); // formatted for easy use on frontend
        }

        public async Task<GetMapsResponse> GetMaps(GetMapsRequest request)
        {
            List<string> queryParams = new List<string>()
            {
                $"secret={AdminSecret}",
                $"offset={request.Offset}",
                $"limit={request.Limit}"
            };

            if (!string.IsNullOrEmpty(request.Filter))
            {
                queryParams.Add($"filter={request.Filter}");
            }

            var response = await _httpClient
                .GetAsync($"{MatchmakingApiUrl}/maps?{string.Join("&", queryParams)}");
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var result = JsonConvert.DeserializeObject<GetMapsResponse>(content);
            return result;
        }

        public async Task<MapContract> GetMap(int id)
        {
            var response = await _httpClient.GetAsync($"{MatchmakingApiUrl}/maps/{id}?secret={AdminSecret}");
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var result = JsonConvert.DeserializeObject<MapContract>(content);
            return result;
        }

        public async Task<MapContract> CreateMap(MapContract newMap)
        {
            var httpcontent = new StringContent(SerializeData(newMap), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{MatchmakingApiUrl}/maps/?secret={AdminSecret}", httpcontent);
            if (response.IsSuccessStatusCode)
            {
                return await GetResult<MapContract>(response);
            }

            await HandleMMError(response);
            return null;
        }

        public async Task<MapContract> UpdateMap(int id, MapContract map)
        {
            var httpcontent = new StringContent(SerializeData(map), Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync($"{MatchmakingApiUrl}/maps/{id}?secret={AdminSecret}", httpcontent);

            if (response.IsSuccessStatusCode)
            {
                return await GetResult<MapContract>(response);
            }

            await HandleMMError(response);
            return null;
        }

        public async Task<GetSeasonMapsResponse> GetCurrentSeasonMaps()
        {
            var response = await _httpClient.GetAsync($"{MatchmakingApiUrl}/maps/currentseason");
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var result = JsonConvert.DeserializeObject<GetSeasonMapsResponse>(content);
            return result;
        }

        public async Task<MessageOfTheDay> GetMotd()
        {
            var response = await _httpClient.GetAsync($"{MatchmakingApiUrl}/admin/motd/");
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            return JsonConvert.DeserializeObject<MessageOfTheDay>(content);
        }

        public async Task<HttpStatusCode> SetMotd(MessageOfTheDay motd)
        {
            var httpcontent = new StringContent(JsonConvert.SerializeObject(motd), Encoding.UTF8, "application/json");
            var result = await _httpClient.PostAsync($"{MatchmakingApiUrl}/admin/motd/?secret={AdminSecret}", httpcontent);
            return result.StatusCode;
        }

        public async Task<TournamentsResponse> GetTournaments()
        {
            var result = await _httpClient.GetAsync($"{MatchmakingApiUrl}/tournaments");
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<TournamentsResponse>(content);
            return deserializeObject;
        }

        public async Task<TournamentResponse> GetTournament(string id)
        {
            var result = await _httpClient.GetAsync($"{MatchmakingApiUrl}/tournaments/{id}");
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<TournamentResponse>(content);
            return deserializeObject;
        }

        public async Task<TournamentResponse> GetUpcomingTournament(GateWay gateway)
        {
            var url = $"{MatchmakingApiUrl}/tournaments/upcoming";
            if (gateway != GateWay.Undefined) {
              url += $"?gateway={(int) gateway}";
            }
            var result = await _httpClient.GetAsync(url);
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<TournamentResponse>(content);
            return deserializeObject;
        }

        public async Task<TournamentResponse> RegisterPlayer(string id, string battleTag, Race race)
        {
            var url = $"{MatchmakingApiUrl}/tournaments/{id}/players";

            var data = new
            {
                battleTag = battleTag,
                race = race,
                secret = AdminSecret,
            };
            JsonContent postBody = JsonContent.Create(data);
            var result = await _httpClient.PostAsync(url, postBody);
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<TournamentResponse>(content);
            return deserializeObject;
        }

        public async Task<TournamentResponse> UpdateTournament(string id, TournamentUpdateBody updates)
        {
            var url = $"{MatchmakingApiUrl}/tournaments/{id}";

            dynamic data = new ExpandoObject();
            if (updates.Name != null) {
              data.name = updates.Name;
            }
            if (updates.StartDateTime != null) {
              data.startDateTime = updates.StartDateTime;
            }
            if (updates.State != null) {
              data.state = updates.State;
            }
            if (updates.Gateway != null) {
              data.gateway = updates.Gateway;
            }
            if (updates.Mode != null) {
              data.mode = updates.Mode;
            }
            if (updates.Format != null) {
              data.format = updates.Format;
            }
            if (updates.MapPool != null) {
              data.mapPool = updates.MapPool;
            }
            data.secret = AdminSecret;

            JsonContent patchBody = JsonContent.Create(data);
            var result = await _httpClient.PatchAsync(url, patchBody);
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<TournamentResponse>(content);
            return deserializeObject;
        }

        public async Task<TournamentResponse> CreateTournament(TournamentUpdateBody updates)
        {
            var url = $"{MatchmakingApiUrl}/tournaments";

            dynamic data = new ExpandoObject();
            if (updates.Name != null) {
              data.name = updates.Name;
            }
            if (updates.StartDateTime != null) {
              data.startDateTime = updates.StartDateTime;
            }
            if (updates.Gateway != null) {
              data.gateway = updates.Gateway;
            }
            if (updates.Mode != null) {
              data.mode = updates.Mode;
            }
            if (updates.Format != null) {
              data.format = updates.Format;
            }
            if (updates.MapPool != null) {
              data.mapPool = updates.MapPool;
            }
            data.secret = AdminSecret;

            JsonContent postBody = JsonContent.Create(data);
            var result = await _httpClient.PostAsync(url, postBody);
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<TournamentResponse>(content);
            return deserializeObject;
        }

        private async Task HandleMMError(HttpResponseMessage response)
        {
            var errorReponse = await GetResult<ErrorResponse>(response);
            var errors = errorReponse.Errors.Select(x => $"{x.Param} {x.Message}");

            throw new ValidationException(string.Join(",", errors));
        }

        private async Task<T> GetResult<T>(HttpResponseMessage response)
            where T : class
        {
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var result = JsonConvert.DeserializeObject<T>(content);
            return result;
        }

        private string SerializeData(object data)
        {
            return JsonConvert.SerializeObject(data, _jsonSerializerSettings);
        }

        private List<FormattedQueue> FormatQueueData(List<Queue> allQueues)
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
                                IList<string> playerBattleTagStrings = new List<string>();

                                for (var i = 0; i < playerData.playerData.Count; i++)
                                {
                                    playerBattleTagStrings.Add(playerData.playerData[i].battleTag);
                                }

                                formattedPlayerData.battleTag = string.Join(" / ", playerBattleTagStrings);
                                formattedPlayerData.mmr = Math.Round(Convert.ToDouble(playerData.mmr),0);
                                formattedPlayerData.rd = Math.Round(Convert.ToDouble(playerData.rd),0);
                                formattedPlayerData.quantile = Math.Round(Convert.ToDouble(playerData.quantiles.quantile),3);
                                formattedPlayerData.activityQuantile = Math.Round(Convert.ToDouble(playerData.quantiles.activityQuantile),3);
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
        public double mmr { get; set; }
        public double rd { get; set; }
        public double quantile { get; set; }
        public double activityQuantile { get; set; }
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
        public List<GameMode> gameModes { get; set; }

        public string banReason { get; set; }
        public string Id => battleTag;
    }

    public class TournamentsResponse
    {
        public Tournament[] tournaments { get; set; }
    }

    public class TournamentResponse
    {
        public Tournament tournament { get; set; }
        public string error { get; set; }
    }

    public class TournamentUpdateBody
    {
        public string Name { get; set; }
        public DateTime? StartDateTime { get; set; }
        public GateWay? Gateway { get; set; }
        public GameMode? Mode { get; set; }
        public TournamentFormat? Format { get; set; }
        public TournamentState? State { get; set; }
        public List<int> MapPool { get; set; }
    }
}
