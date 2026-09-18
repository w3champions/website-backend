using System.Collections.Generic;
using W3C.Domain.CommonValueObjects;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.Ladder;

// A player's ladder rank within a specific season/gateway/gameMode context.
// Decoupled from the internal Rank model so it can be reused as League/Ranks features grow.
// Returned only for players who ARE ranked in the context — callers infer "unranked" from absence.
public class RankInContext
{
    public List<PlayerId> Players { get; set; }   // one entry per team member
    public int Season { get; set; }
    public GateWay GateWay { get; set; }
    public GameMode GameMode { get; set; }
    public Race? Race { get; set; }                // per-race for 1v1; null for team modes
    public int League { get; set; }
    public int RankNumber { get; set; }
    public double RankingPoints { get; set; }
    public int Mmr { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Games { get; set; }
}
