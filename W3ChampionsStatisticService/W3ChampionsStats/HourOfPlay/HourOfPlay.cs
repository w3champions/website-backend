using System;

namespace W3ChampionsStatisticService.W3ChampionsStats.HourOfPlay
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