using System;
using System.Collections.Generic;
using W3ChampionsStatisticService.CommonValueObjects;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats
{
    public class PlayerMmrTimeline : IIdentifiable
    {
        public PlayerMmrTimeline(string battleTag, Race race, GateWay gateWay, int season)
        {
            Id = $"{season}_{battleTag}_@{gateWay}_{race}";
            BattleTag = battleTag;
            Race = Race;
            GateWay = gateWay;
            Season = season;
        }
        public List<MmrAtTime> MmrAtTimes = new List<MmrAtTime>();
        public string BattleTag { get; set; }
        public Race Race { get; set; }
        public GateWay GateWay { get; set; }
        public int Season { get; set; }
        public string Id { get; set; }

    }

    public class MmrAtTime
    {
        public MmrAtTime(int mmr, DateTimeOffset mmrTime) {
            Mmr = mmr;
            MmrTime = mmrTime;
        }
        public int Mmr;
        public DateTimeOffset MmrTime;
    }
}
