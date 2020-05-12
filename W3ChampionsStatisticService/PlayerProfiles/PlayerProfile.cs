using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class PlayerProfile
    {
        public static PlayerProfile Create(string battleTag)
        {
            return new PlayerProfile
            {
                Name = battleTag.Split("#")[0],
                BattleTag = battleTag,
                RaceStats = new List<RaceWinLoss>()
                {
                    new RaceWinLoss(Race.HU),
                    new RaceWinLoss(Race.OC),
                    new RaceWinLoss(Race.UD),
                    new RaceWinLoss(Race.NE),
                    new RaceWinLoss(Race.RnD)
                },
                GateWayStats = new List<GameModeStatsPerGateway>()
            };
        }

        [BsonId]
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public List<RaceWinLoss> RaceStats { get; set; }
        public List<GameModeStatsPerGateway> GateWayStats { get; set; }

        public long GetWinsPerRace(Race race)
        {
            var raceStat = RaceStats.Single(r => r.Race == race);
            return raceStat.Wins;
        }

        public long GetLossPerRace(Race race)
        {
            var raceStat = RaceStats.Single(r => r.Race == race);
            return raceStat.Losses;
        }

        public void RecordWin(Race race, GameMode mode, GateWay gateWay, int season, bool won)
        {
            var gameModeStatsPerGateway = GateWayStats.SingleOrDefault(g => g.GateWay == gateWay && g.Season == season);
            if (gameModeStatsPerGateway == null)
            {
                GateWayStats.Insert(0, GameModeStatsPerGateway.Create(GateWay.Asia, season));
                GateWayStats.Insert(0, GameModeStatsPerGateway.Create(GateWay.Europe, season));
                GateWayStats.Insert(0, GameModeStatsPerGateway.Create(GateWay.America, season));
            }
            gameModeStatsPerGateway = GateWayStats.Single(g => g.GateWay == gateWay && g.Season == season);
            gameModeStatsPerGateway.GameModeStats.Single(g => g.Mode == mode).RecordWin(won);
            RaceStats.Single(r => r.Race == race).RecordWin(won);
        }

        public int TotalLosses => GateWayStats.Sum(g => g.GameModeStats.Sum(s => s.Losses));

        public int TotalWins => GateWayStats.Sum(g => g.GameModeStats.Sum(s => s.Wins));
        public static PlayerProfile Default()
        {
            return Create("UnknownPlayer#2");
        }

        public void UpdateRank(
            GameMode mode,
            GateWay gateWay,
            int mmr,
            int rankingPoints,
            int season)
        {
            var gameModeStatsPerGateway = GateWayStats.SingleOrDefault(g => g.GateWay == gateWay && g.Season == season);
            if (gameModeStatsPerGateway == null)
            {
                GateWayStats.Insert(0, GameModeStatsPerGateway.Create(GateWay.Asia, season));
                GateWayStats.Insert(0, GameModeStatsPerGateway.Create(GateWay.Europe, season));
                GateWayStats.Insert(0, GameModeStatsPerGateway.Create(GateWay.America, season));
            }
            gameModeStatsPerGateway = GateWayStats.Single(g => g.GateWay == gateWay && g.Season == season);
            gameModeStatsPerGateway.GameModeStats.Single(g => g.Mode == mode).RecordRanking(mmr, rankingPoints);
        }

        public GameModeStatsPerGateway GetStatForGateway(GateWay gateWay)
        {
            return GateWayStats.FirstOrDefault(g => g.GateWay == gateWay);
        }
    }

    public class GameModeStatsPerGateway
    {
        public static GameModeStatsPerGateway Create(GateWay gateway, int season)
        {
            return new GameModeStatsPerGateway
            {
                GateWay = gateway,
                Season = season,
                GameModeStats = new List<GameModeStat>()
                {
                    new GameModeStat(GameMode.GM_1v1),
                    new GameModeStat(GameMode.GM_2v2_AT),
                    new GameModeStat(GameMode.GM_4v4),
                    new GameModeStat(GameMode.FFA)
                }
            };
        }

        public GateWay GateWay { get; set; }

        public List<GameModeStat> GameModeStats { get; set; }
        public int Season { get; set; }
    }

    public enum GateWay
    {
        Undefined = 0,
        America = 10,
        Europe = 20,
        Asia = 30
    }
}