using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.W3ChampionsStats.GameLengths;

public class MatchupStats
{
  public string MatchupId => Race1 + "_VS_" + Race2 + "_" + Season;
  public string Race1 { get; set; }
  public string Race2 { get; set; }
  public int Season { get; set; }
  public List<GameLength> Lengths { get; set; }
  public int Mmr { get; set; }

  public void Record(int duration)
    {
        var gameLengths = Lengths.Where(m => m.Seconds < duration);
        var ordered = gameLengths.OrderBy(m => m.Seconds);
        var gameLength = ordered.Last();
        gameLength.AddGame();
    }

    public void Apply(int duration)
    {
        Record(duration);
    }

    public static MatchupStats Create(string race1, string race2, int season)
    {
        return new MatchupStats
        {
            Race1 = race1,
            Race2 = race2,
            Season = season,
            Lengths = CreateLengths()
        };
    }

    private static List<GameLength> CreateLengths()
    {
        int interval = 30;
        var iterations = 120;
        var lengths = new List<GameLength>();
        for (var i = 0; i <= iterations; i++)
        {
            lengths.Add(new GameLength {Seconds = i * interval});
        }

        return lengths;
    }

}