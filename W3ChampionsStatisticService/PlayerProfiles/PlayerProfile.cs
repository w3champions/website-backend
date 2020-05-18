using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;

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
                RaceStats = new List<RaceWinLossPerGateway>(),
            };
        }

        [BsonId]
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public List<Season> ParticipatedInSeasons  { get; set; } = new List<Season>();
        public List<RaceWinLossPerGateway> RaceStats { get; set; }

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
    }
}