using System.Collections.Generic;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.Ladder;

// Body for POST api/ladder/ranks-for-players — enrich a page of search results with rank-in-context.
public class RanksForPlayersRequest
{
    // Callers enrich one search page at a time (global-search pages are capped at 20); the bound keeps
    // the anonymous route's $in query small.
    public const int MaxBattleTags = 50;

    public List<string> BattleTags { get; set; }
    public int Season { get; set; }
    public GateWay GateWay { get; set; }
    public GameMode GameMode { get; set; }
}
