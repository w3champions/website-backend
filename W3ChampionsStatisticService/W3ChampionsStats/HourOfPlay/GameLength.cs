using System;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.W3ChampionsStats.RaceAndWinStats;

namespace W3ChampionsStatisticService.W3ChampionsStats.GameLengths
{
    public class HourOfPlay
    {
        public long Games { get; set; }
        public DateTimeOffset Time { get; set; }

        public void AddGame()
        {
            Games++;
        }
    }
}