using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.W3ChampionsStats.GameLengths;

public class GameLengthStat : IIdentifiable
{
    public string Id => GameMode.ToString();
    public GameMode GameMode { get; set; }
    public IDictionary<string, List<GameLength>> LengthsByMmrRange { get; set; }

    private const string ALL_MMR = "all";

    private void Record(int duration, int mmr, GameMode gameMode)
    {
        RecordForKey(duration, ALL_MMR, gameMode);
        RecordForMmrRange(duration, mmr, gameMode);
    }

    private void RecordForKey(int duration, string key, GameMode gameMode)
    {
        if (!LengthsByMmrRange.TryGetValue(key, out var value))
        {
            value = CreateLengths(gameMode);
            LengthsByMmrRange.Add(key, value);
        }
        var gameLengths = value.Where(m => m.Seconds < duration);
        var ordered = gameLengths.OrderBy(m => m.Seconds);
        var gameLength = ordered.Last();
        gameLength.AddGame();
    }

    private void RecordForMmrRange(int duration, int mmr, GameMode gameMode)
    {
        const int mmrInterval = 200;
        var mmrRange = (int) mmr / mmrInterval;
        mmrRange = mmrInterval * mmrRange;
        RecordForKey(duration, mmrRange.ToString(), gameMode);
    }
    
    public void Apply(int duration, int mmr, GameMode gameMode)
    {
        Record(duration, mmr, gameMode);
    }

    public static GameLengthStat Create(GameMode gameMode)
    {
        return new GameLengthStat
        {
            GameMode = gameMode,
            LengthsByMmrRange = new Dictionary<string, List<GameLength>>(),
        };
    }

    private static List<GameLength> CreateLengths(GameMode gameMode)
    {
        GameMode[] modesWithLongGames = [GameMode.FFA, GameMode.GM_SC_FFA_4];
        var interval = modesWithLongGames.Contains(gameMode) ? 60 : 30;
        var iterations = modesWithLongGames.Contains(gameMode) ? 180 : 120;
        var lengths = new List<GameLength>();
        for (var i = 0; i <= iterations; i++)
        {
            lengths.Add(new GameLength {Seconds = i * interval});
        }

        return lengths;
    }
}
