using System;
using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.W3ChampionsStats.HourOfPlay
{
    public class HourOfPlayPerMode
    {
        public GameMode GameMode { get; set; }
        public List<HourOfPlay> PlayTimePerHour { get; set; }
        public DateTimeOffset Day { get; set; }

        public void Record(DateTimeOffset time)
        {
            var gameLengths = PlayTimePerHour.Where(m => m.Time <= time);
            var ordered = gameLengths.OrderBy(m => m.Time);
            var gameLength = ordered.Last();
            gameLength.AddGame();
        }
    }
}
