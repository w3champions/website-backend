using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.Players
{
    public class RaceStats : List<RaceStat>
    {
        public RaceStats()
        {
            Add(new RaceStat(Race.HU));
            Add(new RaceStat(Race.OC));
            Add(new RaceStat(Race.UD));
            Add(new RaceStat(Race.UD));
            Add(new RaceStat(Race.RnD));
        }

        public void RecordGame(Race race, in bool won)
        {
            var gameModeStat = this.Single(s => s.Race == race);
            gameModeStat.Update(won);
        }
    }
}