using System.Collections.Generic;
using W3C.Contracts.GameObjects;
using W3ChampionsStatisticService.Heroes;

namespace W3ChampionsStatisticService.Matches;

public class PlayerOverviewMatches
{
    public Race Race { get; set; }
    public Race? RndRace { get; set; }
    public int OldMmr { get; set; }
    public double? OldMmrQuantile { get; set; }
    public int CurrentMmr { get; set; }
    public string BattleTag { get; set; }
    public string InviteName { get; set; }
    public string Name { get; set; }
    public int MmrGain => CurrentMmr - OldMmr;
    public bool Won { get; set; }
    public int? MatchRanking { get; set; }
    public string Location { get; set; }
    public string CountryCode { get; set; }
    public string Country { get; set; }
    public string Twitch { get; set; }
    public IList<Hero> Heroes { get; set; }
}
