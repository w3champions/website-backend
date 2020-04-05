using System;

namespace W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays
{
    public class GameDay
    {
        public DateTimeOffset Date { get; set; }
        public long GamesPlayed { get; set; }

        public static GameDay Create(DateTimeOffset endTime)
        {
            return new GameDay
            {
                Date = endTime.Date
            };
        }

        public void AddGame()
        {
            GamesPlayed++;
        }
    }
}