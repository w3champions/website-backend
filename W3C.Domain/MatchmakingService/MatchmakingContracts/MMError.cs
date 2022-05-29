using Newtonsoft.Json;

namespace W3C.Domain.MatchmakingService.MatchmakingContracts
{
    public class MMError
    {
        [JsonProperty("msg")]
        public string Message { get; set; }

        public string Param { get; set; }
    }
}
