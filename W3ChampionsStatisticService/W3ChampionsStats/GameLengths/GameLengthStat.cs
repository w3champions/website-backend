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

    public void Apply(TimeSpan duration)
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
        var gameModeConfig = GetGameModeLengthConfiguration(gameMode);
        var lengths = new List<GameLength>();

        for (var i = 0; i <= gameModeConfig.Iterations; i++)
        {
            lengths.Add(new GameLength { Seconds = i * gameModeConfig.Interval });
        }

        return lengths;
    }

    private static GameModeLengthConfiguration GetGameModeLengthConfiguration(GameMode gameMode)
    {
        // Default configuration
        var defaultConfig = new GameModeLengthConfiguration(30, 120);

        // Game mode specific configurations
        var configurations = new Dictionary<GameMode, GameModeLengthConfiguration>
        {
            { GameMode.FFA, new GameModeLengthConfiguration(60, 180) },         // 180 min
            { GameMode.GM_SC_FFA_4, new GameModeLengthConfiguration(60, 120) }, // 120min
            { GameMode.GM_SC_OZ, new GameModeLengthConfiguration(60, 120) },    // 120min
            { GameMode.GM_CF, new GameModeLengthConfiguration(45, 133) },       // 100min
        };

        // This will only take effect if you delete the GameLengthStat DB document for that game mode.
        // This will result in the loss of historical data for that game mode as reprocessing matches
        // is not an option at the moment.
        // The only option to reduce the timeframe without data loss is to preserve the same 
        // interval length and to only delete the length buckets at the top end from the document.

        return configurations.TryGetValue(gameMode, out var config) ? config : defaultConfig;
    }

    private class GameModeLengthConfiguration(int Interval, int Iterations)
    {
        public int Interval { get; } = Interval;
        public int Iterations { get; } = Iterations;
    }
}
