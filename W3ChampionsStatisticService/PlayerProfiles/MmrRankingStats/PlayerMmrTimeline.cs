using System;
using System.Collections.Generic;
using W3ChampionsStatisticService.CommonValueObjects;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.ReadModelBase;
using System.Collections;

namespace W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats
{
    public class PlayerMmrTimeline : IIdentifiable
    {
        public PlayerMmrTimeline(string battleTag, Race race, GateWay gateWay, int season, GameMode gameMode)
        {
            Id = $"{season}_{battleTag}_@{gateWay}_{race}_{gameMode}";
            BattleTag = battleTag;
            Race = race;
            GateWay = gateWay;
            Season = season;
            GameMode = gameMode;
        }
        public List<MmrAtTime> MmrAtTimes = new List<MmrAtTime>();
        public string BattleTag { get; set; }
        public Race Race { get; set; }
        public GateWay GateWay { get; set; }
        public int Season { get; set; }
        public GameMode GameMode { get; set; }
        public string Id { get; set; }

        public void AddSorted(MmrAtTime mmrAtTime)
        {
            if (MmrAtTimes.Count == 0)
            {
                MmrAtTimes.Add(mmrAtTime);
                return;
            }
            if (MmrAtTimes[MmrAtTimes.Count - 1].MmrTime <= mmrAtTime.MmrTime)
            {
                MmrAtTimes.Add(mmrAtTime);
                return;
            }
                if (MmrAtTimes[0].MmrTime >= mmrAtTime.MmrTime)
            {
                MmrAtTimes.Insert(0, mmrAtTime);
                return;
            }
            int index = MmrAtTimes.BinarySearch(mmrAtTime);
            if (index < 0)
                index = ~index;
            MmrAtTimes.Insert(index, mmrAtTime);
        }
    }

    public class MmrAtTime : IComparable
    {
        public MmrAtTime(int mmr, DateTimeOffset mmrTime) {
            Mmr = mmr;
            MmrTime = mmrTime;
        }
        public int Mmr;
        public DateTimeOffset MmrTime;

        public int CompareTo(object obj)
        {
            DateTimeOffset mmrTime_obj = ((MmrAtTime)obj).MmrTime;
            if (this.MmrTime < mmrTime_obj)
                return -1;
            if (this.MmrTime > mmrTime_obj)
                return 1;
            return 0;
        }
    }
}
