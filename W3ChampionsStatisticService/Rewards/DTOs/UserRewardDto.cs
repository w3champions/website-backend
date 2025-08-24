using System;

namespace W3ChampionsStatisticService.Rewards.DTOs;

public class UserRewardDto
{
    public string Id { get; set; }
    public string DisplayId { get; set; }
    public string ModuleId { get; set; }
    public string ModuleName { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
