using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerInfoForGlobalSearch
    {
        [JsonIgnore]
        public RankInfo RankInfo { get; set; }
        [JsonIgnore]
        public List<PlayerOverview> Players { get; set; }
        public PlayerOverview Player => Players?.SingleOrDefault();
    }

    public class RankInfo
    {
        public int League { get; set; }
        public int Season { get; set; }
        public GameMode GameMode { get; set; }
        public int RankNumber { get; set; }
    }
}