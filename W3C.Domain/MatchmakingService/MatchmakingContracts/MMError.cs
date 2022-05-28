using Newtonsoft.Json;

namespace W3ChampionsStatisticService.MatchmakingData.MatchmakingContracts
{
    public class MMError
    {
        [JsonProperty("msg")]
        public string Message { get; set; }

        public string Param { get; set; }
    }
}
