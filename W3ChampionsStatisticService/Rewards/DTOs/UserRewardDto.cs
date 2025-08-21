using System;

namespace W3ChampionsStatisticService.Rewards.DTOs;

public class UserRewardDto
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Type { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}