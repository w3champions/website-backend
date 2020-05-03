using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Ladder
{
    public class Rank : IIdentifiable
    {
        public Rank(int gateway, int league, int rankNumber, int rankingPoints, string playerId, GameMode gameMode)
        {
            Gateway = gateway;
            League = league;
            RankNumber = rankNumber;
            RankingPoints = rankingPoints;
            PlayerId = playerId;
            GameMode = gameMode;
        }

        public int Gateway { get; set; }
        public GameMode GameMode { get; set; }
        public string Id => $"{PlayerId}_{GameMode}";
        public int League { get; set; }
        public int LeagueOrder { get; set; }
        public int RankNumber { get; set; }
        public int RankingPoints { get; set; }
        public string PlayerId { get; set; }
        [JsonIgnore]
        public List<PlayerOverview> Players { get; set; }
        public PlayerOverview Player => Players.SingleOrDefault();
    }
}