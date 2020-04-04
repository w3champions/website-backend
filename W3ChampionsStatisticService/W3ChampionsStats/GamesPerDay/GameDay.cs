using System;

namespace W3ChampionsStatisticService.W3ChampionsStats.GamesPerDay
{
    public class GameDay
    {
        public DateTime Date { get; set; }
        public long GamesPlayed { get; set; }

        public static GameDay Create(DateTime endTime)
        {
            return new GameDay
            {
                Date = endTime
            };
        }

        public void AddGame()
        {
            GamesPlayed++;
        }
    }
}