using System;
using System.Collections.Generic;

namespace W3ChampionsStatisticService.Rewards.DTOs;

public class PatreonAccountLinkDto
{
    public string Id { get; set; }
    public string BattleTag { get; set; }
    public string PatreonUserId { get; set; }
    public DateTime LinkedAt { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
}
