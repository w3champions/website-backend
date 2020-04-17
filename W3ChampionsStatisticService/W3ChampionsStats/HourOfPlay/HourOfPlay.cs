using System;

namespace W3ChampionsStatisticService.W3ChampionsStats.HourOfPlay
{
    public class HourOfPlay
    {
        public long Games { get; set; }
        public DateTime Time { get; set; }

        public void AddGame()
        {
            Games++;
        }
    }
}