using System.Collections.Generic;

namespace W3ChampionsStatisticService.Ladder;

public class CountryRanking(int league, string name, int division, int order, IEnumerable<Rank> ranks)
{
    public int League { get; private set; } = league;
    public string LeagueName { get; private set; } = name;
    public int LeagueDivision { get; private set; } = division;
    public int LeagueOrder { get; private set; } = order;
    public IEnumerable<Rank> Ranks { get; private set; } = ranks;
}
