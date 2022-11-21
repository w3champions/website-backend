using System.Collections.Generic;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays
{
    public class GameDayGroup
    {
        public GameMode GameMode { get; }
        public List<GamesPerDay> GameDays { get; }

        public GameDayGroup(GameMode gameMode, List<GamesPerDay> gameDays)
        {
            GameMode = gameMode;
            GameDays = gameDays;
        }
    }
}
