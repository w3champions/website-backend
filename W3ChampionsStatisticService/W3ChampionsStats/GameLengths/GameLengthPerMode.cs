using System;
using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.W3ChampionsStats.GameLengths
{
    public class GameLengthPerMode
    {
        public GameMode GameMode { get; set; }
        public List<GameLength> Lengths { get; set; }

        public void Record(TimeSpan duration)
        {
            var gameLengths = Lengths.Where(m => m.passedTimeInSeconds < duration.TotalSeconds);
            var ordered = gameLengths.OrderBy(m => m.passedTimeInSeconds);
            var gameLength = ordered.Last();
            gameLength.AddGame();
        }
    }
}