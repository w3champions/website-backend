using System.Collections.Generic;
using W3C.Contracts.GameObjects;
using W3C.Domain.MatchmakingService;

namespace W3ChampionsStatisticService.Matches;

public class PlayerOverviewMatches
{
    public Race Race { get; set; }
    public Race? RndRace { get; set; }
    public int? OldMmr { get; set; }
    public double? OldMmrQuantile { get; set; }
    public double? OldRankDeviation { get; set; }
    public int? CurrentMmr { get; set; }
    public string BattleTag { get; set; }
    public string InviteName { get; set; }
    public string Name { get; set; }
    public int? MmrGain => CurrentMmr != null && CurrentMmr > 0 && OldMmr != null ? CurrentMmr - OldMmr : null;
    public bool Won { get; set; }
    public int? MatchRanking { get; set; }
    public string Location { get; set; }
    public string CountryCode { get; set; }
    public string Country { get; set; }
    public string Twitch { get; set; }
    public IList<Heroes.Hero> Heroes { get; set; }
    public Ranking Ranking { get; set; }
}
