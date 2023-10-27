using System;
using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.W3ChampionsStats.GameLengths;

public class GameLengthStat : IIdentifiable
{
    public string Id => GameMode.ToString();
    public GameMode GameMode { get; set; }
    public List<GameLength> Lengths { get; set; }

    public void Record(TimeSpan duration)
    {
        var gameLengths = Lengths.Where(m => m.Seconds < duration.TotalSeconds);
        var ordered = gameLengths.OrderBy(m => m.Seconds);
        var gameLength = ordered.Last();
        gameLength.AddGame();
    }

    public void Apply(GameMode gameMode, TimeSpan duration)
    {
        Record(duration);
    }

    public static GameLengthStat Create(GameMode mode)
    {
        return new GameLengthStat
        {
            GameMode = mode,
            Lengths = CreateLengths(mode)
        };
    }

    private static List<GameLength> CreateLengths(GameMode gameMode)
    {
        GameMode[] modesWithLongGames = { GameMode.FFA, GameMode.GM_SC_FFA_4 };
        int interval = modesWithLongGames.Contains(gameMode) ? 60 : 30;
        var iterations = modesWithLongGames.Contains(gameMode) ? 180 : 120;
        var lengths = new List<GameLength>();
        for (var i = 0; i <= iterations; i++)
        {
            lengths.Add(new GameLength {Seconds = i * interval});
        }

        return lengths;
    }
}
