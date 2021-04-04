using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerInfoForProxy
    {
        public GameMode GameMode { get; set; }
        [JsonIgnore]
        public List<PlayerOverview> Players { get; set; }
        public PlayerOverview Player => Players?.SingleOrDefault();
    }
}