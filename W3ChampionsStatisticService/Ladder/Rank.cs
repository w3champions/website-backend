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
            string playerId,
            League league,
            int rankNumber,
            int rankingPoints,
            GateWay gateway,
            GameMode gameMode,
            int season)
        {
            Gateway = gateway;
            League = league.Id;
            LeagueOrder = league.Order;
            LeagueName = league.Name;
            LeagueDivision = league.Division;
            RankNumber = rankNumber;
            RankingPoints = rankingPoints;
            PlayerId = playerId;
            PlayerIdToLower = playerId.ToLower();
            GameMode = gameMode;
            Season = season;
            PlayersInfo = new List<PlayerInfo>();
        }

        public GateWay Gateway { get; set; }
        public string Id => PlayerId;
        public int League { get; set; }
        public int LeagueOrder { get; set; }
        public string LeagueName { get; set; }
        public int LeagueDivision { get; set; }
        public int RankNumber { get; set; }
        public int RankingPoints { get; set; }
        public string PlayerId { get; set; }
        [JsonIgnore]
        public string PlayerIdToLower { get; set; }
        [JsonIgnore]
        public List<PlayerOverview> Players { get; set; }
        public PlayerOverview Player => Players.SingleOrDefault();
        public GameMode GameMode { get; set; }
        public int Season { get; set; }

        [BsonIgnore]
        public List<PlayerInfo> PlayersInfo { get; set; }

    }
}