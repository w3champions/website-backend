using System;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays
{
    public class GamesPerDay : IIdentifiable
    {
        public DateTimeOffset Date { get; set; }
        public long GamesPlayed { get; set; }
        public string Id => Date.ToString("yyyy-MM-dd");

        public static GamesPerDay Create(DateTimeOffset endTime)
        {
            return new GamesPerDay
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