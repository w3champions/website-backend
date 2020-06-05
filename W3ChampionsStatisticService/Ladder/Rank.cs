using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Ladder
{
    public class Rank : IIdentifiable
    {
        public Rank(
            List<string> playerIds,
            int league,
            int rankNumber,
            int rankingPoints,
            GateWay gateway,
            GameMode gameMode,
            int season)
        {
            Gateway = gateway;
            League = league;
            RankNumber = rankNumber;
            RankingPoints = rankingPoints;
            var btags = playerIds.Select(b => $"{b}@{(int) gateway}").OrderBy(t => t);
            var createPlayerId = $"{season}_{string.Join("_", btags)}_{gameMode}";
            PlayerId = createPlayerId;
            GameMode = gameMode;
            Season = season;

            Player1Id = playerIds.FirstOrDefault();
            Player2Id = playerIds.Skip(1).FirstOrDefault();
        }

        public GateWay Gateway { get; set; }
        public string Id => PlayerId;
        public int League { get; set; }
        public int LeagueDivision { get; set; }
        public string LeagueName { get; set; }
        public int LeagueOrder { get; set; }
        public int RankNumber { get; set; }
        public int RankingPoints { get; set; }
        public string PlayerId { get; set; }
        public string Player1Id { get; set; }
        public string Player2Id { get; set; }
        [JsonIgnore]
        public List<PlayerOverview> Players { get; set; }
        public PlayerOverview Player => Players?.SingleOrDefault();
        public GameMode GameMode { get; set; }
        public int Season { get; set; }

        [BsonIgnore]
        public List<PlayerInfo> PlayersInfo { get; set; } = new List<PlayerInfo>();
    }
}