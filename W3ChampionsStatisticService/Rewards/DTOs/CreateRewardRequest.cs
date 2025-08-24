using System.Collections.Generic;
using W3C.Domain.Rewards.ValueObjects;

namespace W3ChampionsStatisticService.Rewards.DTOs;

public class CreateRewardRequest
{
    public string DisplayId { get; set; }
    public string ModuleId { get; set; }
    public Dictionary<string, object> Parameters { get; set; }
    public RewardDuration Duration { get; set; }
}