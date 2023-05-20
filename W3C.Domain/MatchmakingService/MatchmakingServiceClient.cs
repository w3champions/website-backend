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
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking.Tournaments;
using W3C.Contracts.Matchmaking;
using W3C.Contracts.Matchmaking.Queue;
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
            var response = await _httpClient.GetAsync($"{MatchmakingApiUrl}/admin/bannedPlayers?secret={AdminSecret}");
            if (response.IsSuccessStatusCode)
            {
                return await GetResult<BannedPlayerResponse>(response);
            }

            await HandleMMError(response);
            return null;
        }

        public async Task<HttpResponseMessage> PostBannedPlayer(BannedPlayerReadmodel bannedPlayerReadmodel)
        {
            var httpcontent = new StringContent(JsonConvert.SerializeObject(bannedPlayerReadmodel), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{MatchmakingApiUrl}/admin/bannedPlayers?secret={AdminSecret}", httpcontent);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            await HandleMMError(response);
            return null;
        }

        public async Task<HttpResponseMessage> DeleteBannedPlayer(BannedPlayerReadmodel bannedPlayerReadmodel)
        {
            var encodedTag = HttpUtility.UrlEncode(bannedPlayerReadmodel.battleTag);
            var response = await _httpClient.DeleteAsync($"{MatchmakingApiUrl}/admin/bannedPlayers/{encodedTag}?secret={AdminSecret}");
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            await HandleMMError(response);
            return null;
        }

        public async Task<List<MappedQueue>> GetLiveQueueData()
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
                $"secret={AdminSecret}"
            };

            if (!string.IsNullOrEmpty(request.Filter))
            {
                queryParams.Add($"filter={request.Filter}");
            }

            var response = await _httpClient.GetAsync($"{MatchmakingApiUrl}/maps?{string.Join("&", queryParams)}");
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

        public async Task<GetMapsResponse> GetTournamentMaps(bool? active)
        {
            var url = $"{MatchmakingApiUrl}/maps/tournaments";
            if (active.HasValue) {
                string activeStr = active.Value ? "true" : "false";
                url += $"?active={activeStr}";
            }
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var result = JsonConvert.DeserializeObject<GetMapsResponse>(content);
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

        public async Task<List<ActiveGameMode>> GetCurrentlyActiveGameModes() {
            var response = await _httpClient.GetAsync($"{MatchmakingApiUrl}/ladder/active-modes?secret={AdminSecret}");
            if (response.IsSuccessStatusCode) {
                var content = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(content)) return null;
                var deserializeObject = JsonConvert.DeserializeObject<List<ActiveGameMode>>(content);
                return deserializeObject;
            } else {
                return null;
            }
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

        public async Task<TournamentResponse> RegisterPlayer(string id, string battleTag, Race race, string countryCode)
        {
            var url = $"{MatchmakingApiUrl}/tournaments/{id}/players";

            var data = new
            {
                battleTag = battleTag,
                race = race,
                countryCode = countryCode,
                secret = AdminSecret,
            };
            JsonContent postBody = JsonContent.Create(data);
            var result = await _httpClient.PostAsync(url, postBody);
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<TournamentResponse>(content);
            return deserializeObject;
        }

        public async Task<TournamentResponse> UnregisterPlayer(string id, string battleTag)
        {
            var url = $"{MatchmakingApiUrl}/tournaments/{id}/players";
            var data = new
            {
                battleTag = battleTag,
                secret = AdminSecret,
            };
            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
            var result = await _httpClient.SendAsync(request);
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
            if (updates.RegistrationTimeMinutes != null) {
              data.registrationTimeMinutes = updates.RegistrationTimeMinutes;
            }
            if (updates.ReadyTimeSeconds != null) {
              data.readyTimeSeconds = updates.ReadyTimeSeconds;
            }
            if (updates.VetoTimeSeconds != null) {
              data.vetoTimeSeconds = updates.VetoTimeSeconds;
            }
            if (updates.ShowWinnerTimeHours != null) {
              data.showWinnerTimeHours = updates.ShowWinnerTimeHours;
            }
            data.matcherinoUrl = updates.MatcherinoUrl;
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
            if (updates.RegistrationTimeMinutes != null) {
              data.registrationTimeMinutes = updates.RegistrationTimeMinutes;
            }
            if (updates.ReadyTimeSeconds != null) {
              data.readyTimeSeconds = updates.ReadyTimeSeconds;
            }
            if (updates.VetoTimeSeconds != null) {
              data.vetoTimeSeconds = updates.VetoTimeSeconds;
            }
            if (updates.ShowWinnerTimeHours != null) {
              data.showWinnerTimeHours = updates.ShowWinnerTimeHours;
            }
            data.matcherinoUrl = updates.MatcherinoUrl;
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
            throw new HttpRequestException(string.Join(",", errors), null, response.StatusCode);
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

        private List<MappedQueue> FormatQueueData(List<Queue> allQueues)
        {
            try
            {
                var formattedAllQueueData = new List<MappedQueue>();
                if (allQueues != null) {
                    foreach (var queue in allQueues)
                    {
                        var gameModeQueue = new MappedQueue();
                        gameModeQueue.gameMode = queue.gameMode;
                        
                        var formattedSingleQueueData = new List<MappedPlayerData>();

                        if (queue.snapshot.Count > 0) {
                            foreach (var playerData in queue.snapshot)
                            {
                                var mappedPlayerData = new MappedPlayerData();

                                // if it's an AT, data is taken from only 1 player
                                IList<string> playerBattleTagStrings = new List<string>();

                                for (var i = 0; i < playerData.playerData.Count; i++)
                                {
                                    playerBattleTagStrings.Add(playerData.playerData[i].battleTag);
                                }

                                mappedPlayerData.battleTag = string.Join(" / ", playerBattleTagStrings);
                                mappedPlayerData.mmr = Math.Round(Convert.ToDouble(playerData.mmr),0);
                                mappedPlayerData.rd = Math.Round(Convert.ToDouble(playerData.rd),0);
                                mappedPlayerData.quantile = Math.Round(Convert.ToDouble(playerData.quantiles.quantile),3);
                                mappedPlayerData.activityQuantile = Math.Round(Convert.ToDouble(playerData.quantiles.activityQuantile),3);
                                mappedPlayerData.queueTime = playerData.queueTime;
                                mappedPlayerData.isFloConnected = playerData.isFloConnected;
                                mappedPlayerData.location = playerData.playerData[0].location;
                                mappedPlayerData.serverOption = playerData.playerData[0].serverOption;
                                
                                formattedSingleQueueData.Add(mappedPlayerData);
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
                return new List<MappedQueue>();
            }

        }
    }

    public class MappedQueue
    {
        public int gameMode { get; set; }
        public List<MappedPlayerData> snapshot { get; set; }
    }

    public class MappedPlayerData
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
        public List<GameMode> gameModes { get; set; }
        public string banReason { get; set; }
        public string Id => battleTag;
        public string banInsertDate { get; set; }
        public string author { get; set;}
    }

    public class ActiveGameMode
    {
        public GameMode id { get; set; }
        public List<MapShortInfo> maps { get; set; }
        public string name { get; set; }
        public string type { get; set; }
    }

    public class MapShortInfo
    {
        public int id { get; set; }
        public string name { get; set; }
        public string path { get; set; }
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
        public string MatcherinoUrl { get; set; }
        public int? RegistrationTimeMinutes { get; set; }
        public int? ReadyTimeSeconds { get; set; }
        public int? VetoTimeSeconds { get; set; }
        public int? ShowWinnerTimeHours { get; set; }
    }
}
