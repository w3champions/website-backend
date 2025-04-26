using System.Collections.Generic;
using System.Linq;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.W3ChampionsStats.GameLengths;

namespace W3ChampionsStatisticService.W3ChampionsStats.MatchupLengths;
public class MatchupLength : IIdentifiable
{
    public string Id => CompoundNormalizedId(Race1, Race2, Season);
    public string Race1 { get; set; }
    public string Race2 { get; set; }
    public string Season { get; set; }
    public Dictionary<string, List<GameLength>> LengthsByMmrRange { get; set; }

    private static readonly string ALL_MMR = "all";

    public static string CompoundNormalizedId(string race1, string race2, string season)
    {
        var races = new List<string> { race1.ToLower(), race2.ToLower() };
        races.Sort();
        return races[0] + "_vs_" + races[1] + "_" + season;
    }

    public void Record(int duration, int mmr)
    {
        RecordForKey(duration, ALL_MMR);
        RecordForMmrRange(duration, mmr);
    }

    private void RecordForKey(int duration, string key)
    {
        if (!LengthsByMmrRange.ContainsKey(key))
        {
            LengthsByMmrRange.Add(key, CreateLengths());
        }
        var gameLengths = LengthsByMmrRange[key].Where(m => m.Seconds < duration);
        var ordered = gameLengths.OrderBy(m => m.Seconds);
        var gameLength = ordered.Last();
        gameLength.AddGame();
    }

    private void RecordForMmrRange(int duration, int mmr)
    {
        var mmrInterval = 200;
        var mmrRange = (int)mmr / mmrInterval;
        mmrRange = mmrInterval * mmrRange;
        RecordForKey(duration, mmrRange.ToString());
    }

    public void Apply(int duration, int mmr)
    {
        Record(duration, mmr);
    }

    public static MatchupLength Create(string race1, string race2, string season)
    {
        return new MatchupLength
        {
            Race1 = race1,
            Race2 = race2,
            Season = season,
            LengthsByMmrRange = new Dictionary<string, List<GameLength>>(),
        };
    }

    private static List<GameLength> CreateLengths()
    {
        int interval = 30;
        var iterations = 120;
        var lengths = new List<GameLength>();
        for (var i = 0; i <= iterations; i++)
        {
            lengths.Add(new GameLength { Seconds = i * interval });
        }

        return lengths;
    }

}
