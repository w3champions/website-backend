using System.Collections.Generic;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays;

public class GameDayGroup(GameMode gameMode, List<GamesPerDay> gameDays)
{
    public GameMode GameMode { get; } = gameMode;
    public List<GamesPerDay> GameDays { get; } = gameDays;
}
