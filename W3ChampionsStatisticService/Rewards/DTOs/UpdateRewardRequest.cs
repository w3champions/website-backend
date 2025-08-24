using System.Collections.Generic;
using W3C.Domain.Rewards.ValueObjects;

namespace W3ChampionsStatisticService.Rewards.DTOs;

public class UpdateRewardRequest
{
    public string DisplayId { get; set; }
    public Dictionary<string, object> Parameters { get; set; }
    public RewardDuration Duration { get; set; }
    public bool? IsActive { get; set; }
}