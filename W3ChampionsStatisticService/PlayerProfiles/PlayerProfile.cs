using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class PlayerProfile
    {
        private List<Season> _participatedInSeasons = new List<Season>();

        public static PlayerProfile Create(string battleTag)
        {
            return new PlayerProfile
            {
                Name = battleTag.Split("#")[0],
                BattleTag = battleTag,
                RaceStats = new List<RaceWinLossPerGateway>(),
                GateWayStats = new List<GameModeStatsPerGateway>()
            };
        }

        [BsonId]
        public string BattleTag { get; set; }
        public string Name { get; set; }

        // do this until sorted everywhere
        public List<Season> ParticipatedInSeasons
        {
            get => _participatedInSeasons.OrderByDescending(s => s.Id).ToList();
            set => _participatedInSeasons = value;
        }

        public List<RaceWinLossPerGateway> RaceStats { get; set; }
        public List<GameModeStatsPerGateway> GateWayStats { get; set; }

        public long GetWinsPerRace(Race race)
        {
            return RaceStats.Where(r => r.Race == race).Sum(r => r.Wins);
        }

        public long GetLossPerRace(Race race)
        {
            return RaceStats.Where(r => r.Race == race).Sum(r => r.Losses);
        }

        public void RecordWin(Race race, GameMode mode, GateWay gateWay, int season, bool won)
        {
            if (!ParticipatedInSeasons.Select(s => s.Id).Contains(season))
            {
                ParticipatedInSeasons.Insert(0, new Season(season));
            }

            var gameModeStatsPerGateway = GateWayStats.SingleOrDefault(g => g.GateWay == gateWay && g.Season == season);
            if (gameModeStatsPerGateway == null)
            {
                GateWayStats.Insert(0, GameModeStatsPerGateway.Create(GateWay.Asia, season));
                GateWayStats.Insert(0, GameModeStatsPerGateway.Create(GateWay.Europe, season));
                GateWayStats.Insert(0, GameModeStatsPerGateway.Create(GateWay.America, season));
            }
            gameModeStatsPerGateway = GateWayStats.Single(g => g.GateWay == gateWay && g.Season == season);
            gameModeStatsPerGateway.GameModeStats.Single(g => g.Mode == mode).RecordWin(won);

            var raceStatsPerGateway = RaceStats.SingleOrDefault(g => g.GateWay == gateWay && g.Season == season && g.Race == race);
            if (raceStatsPerGateway == null)
            {
                RaceStats.Insert(0, new RaceWinLossPerGateway(Race.RnD, GateWay.Asia, season));
                RaceStats.Insert(0, new RaceWinLossPerGateway(Race.UD, GateWay.Asia, season));
                RaceStats.Insert(0, new RaceWinLossPerGateway(Race.NE, GateWay.Asia, season));
                RaceStats.Insert(0, new RaceWinLossPerGateway(Race.OC, GateWay.Asia, season));
                RaceStats.Insert(0, new RaceWinLossPerGateway(Race.HU, GateWay.Asia, season));

                RaceStats.Insert(0, new RaceWinLossPerGateway(Race.RnD, GateWay.Europe, season));
                RaceStats.Insert(0, new RaceWinLossPerGateway(Race.UD, GateWay.Europe, season));
                RaceStats.Insert(0, new RaceWinLossPerGateway(Race.NE, GateWay.Europe, season));
                RaceStats.Insert(0, new RaceWinLossPerGateway(Race.OC, GateWay.Europe, season));
                RaceStats.Insert(0, new RaceWinLossPerGateway(Race.HU, GateWay.Europe, season));

                RaceStats.Insert(0, new RaceWinLossPerGateway(Race.RnD, GateWay.America, season));
                RaceStats.Insert(0, new RaceWinLossPerGateway(Race.UD, GateWay.America, season));
                RaceStats.Insert(0, new RaceWinLossPerGateway(Race.NE, GateWay.America, season));
                RaceStats.Insert(0, new RaceWinLossPerGateway(Race.OC, GateWay.America, season));
                RaceStats.Insert(0, new RaceWinLossPerGateway(Race.HU, GateWay.America, season));
            }
            raceStatsPerGateway = RaceStats.Single(g => g.GateWay == gateWay && g.Season == season && g.Race == race);
            raceStatsPerGateway.RecordWin(won);
        }

        public int TotalLosses => GateWayStats.Sum(g => g.GameModeStats.Sum(s => s.Losses));

        public int TotalWins => GateWayStats.Sum(g => g.GameModeStats.Sum(s => s.Wins));

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
}