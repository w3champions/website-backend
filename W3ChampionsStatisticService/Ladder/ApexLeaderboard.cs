using System.Collections.Generic;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Ladder;

public class ApexLeaderboard : IIdentifiable
{
    public string Id { get; set; }
    public int Season { get; set; }
    public GameMode GameMode { get; set; }
    public int? CutoffApexPoints { get; set; }
    public int GmCount { get; set; }
    public List<ApexLeaderboardEntry> Players { get; set; }
}

public class ApexLeaderboardEntry
{
    public List<string> BattleTags { get; set; }
    public Race? Race { get; set; }
    public int ApexPoints { get; set; }
    public int League { get; set; }
    public int RankNumber { get; set; }
}
