using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.Matches;

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
                RaceStats = new RaceStats
                {
                    new RaceStat(Race.HU),
                    new RaceStat(Race.OC),
                    new RaceStat(Race.UD),
                    new RaceStat(Race.NE),
                    new RaceStat(Race.RnD)
                },
                GateWayStats = new List<GameModeStatsPerGateway>()
                {
                    GameModeStatsPerGateway.Create(GateWay.Usa),
                    GameModeStatsPerGateway.Create(GateWay.Europe),
                    GameModeStatsPerGateway.Create(GateWay.Asia),
                }
            };
        }

        [BsonId]
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public RaceStats RaceStats { get; set; }
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

        public void RecordWin(Race race, GameMode mode, GateWay gateWay, bool won)
        {
            GateWayStats.Single(g => g.GateWay == gateWay).GameModeStats.RecordGame(mode, won);
            RaceStats.RecordGame(race, won);
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
            int rankingPoints)
        {
            GateWayStats.Single(g => g.GateWay == gateWay).GameModeStats.RecordRanking(mode, mmr, rankingPoints);
        }
    }

    public class GameModeStatsPerGateway
    {
        public static GameModeStatsPerGateway Create(GateWay gateway)
        {
            return new GameModeStatsPerGateway
            {
                GateWay = gateway,
                GameModeStats = new GameModeStats
                {
                    new GameModeStat(GameMode.GM_1v1),
                    new GameModeStat(GameMode.GM_2v2_AT),
                    new GameModeStat(GameMode.GM_4v4),
                    new GameModeStat(GameMode.FFA)
                }
            };
        }

        public GateWay GateWay { get; set; }

        public GameModeStats GameModeStats { get; set; }
    }

    public enum GateWay
    {
        Undefined = 0,
        Usa = 10,
        Europe = 20,
        Asia = 30
    }
}