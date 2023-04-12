using System;
using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.W3ChampionsStats.HourOfPlay
{
    public class HourOfPlayPerMode
    {
        public List<HourOfPlay> PlayTimePerHour { get; set; }
        public DateTime Day { get; set; }

        public void Record(DateTime time)
        {
            var gameLengths = PlayTimePerHour.Where(m => m.Time <= time);
            var ordered = gameLengths.OrderBy(m => m.Time);
            var gameLength = ordered.Last();
            gameLength.AddGame();
        }
    }
}
