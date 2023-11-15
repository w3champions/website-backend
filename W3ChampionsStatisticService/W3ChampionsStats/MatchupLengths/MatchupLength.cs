using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.W3ChampionsStats.MatchupLengths;

public class MatchupLength : IIdentifiable
{
  public string Id => BattleTag + "_" + Season.ToString();

  public string MatchupStats { get; set; }
  public string BattleTag { get; set; }
  public int Season { get; set; }
}