using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Ladder;

public class CountryRanking
{
    public CountryRanking(int league, string name, int division, int order, IEnumerable<Rank> ranks)
    {
        League = league;
        LeagueName = name;
        LeagueDivision = division;
        LeagueOrder = order;
        Ranks = ranks;
    }

    public int League { get; private set; }
    public string LeagueName { get; private set; }
    public int LeagueDivision { get; private set; }
    public int LeagueOrder { get; private set; }
    public IEnumerable<Rank> Ranks { get; private set; }
}
