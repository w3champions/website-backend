using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class RaceStats : List<RaceStat>
    {
        public void RecordGame(Race race, in bool won)
        {
            var gameModeStat = this.Single(s => s.Race == race);
            gameModeStat.Update(won);
        }
    }
}