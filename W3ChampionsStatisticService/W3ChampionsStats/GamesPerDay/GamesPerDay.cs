using System;
using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.MatchEvents;

namespace W3ChampionsStatisticService.W3ChampionsStats.GamesPerDay
{
    public class GamesPerDay
    {
        public void Apply(Match match)
        {
            var endTime = DateTimeOffset.FromUnixTimeMilliseconds(match.endTime).Date;
            var today = GameDays.SingleOrDefault(g => g.Date == endTime);
            if (today == null)
            {
                GameDays.Add(GameDay.Create(endTime));
            }
            
            var todayNew = GameDays.Single(g => g.Date == DateTimeOffset.Now.Date);
            todayNew.AddGame();
        }

        public List<GameDay> GameDays { get; set; } = new List<GameDay>();
        public string Id { get; set; } = "GamesPerDayStats";
    }
}