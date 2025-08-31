using System;
using System.Collections.Generic;
using W3C.Domain.Rewards.ValueObjects;

namespace W3ChampionsStatisticService.Rewards.DTOs;

public class UserRewardDto
{
    // Reward Assignment properties
    public string AssignmentId { get; set; }
    public string UserId { get; set; }
    public string RewardId { get; set; }
    public string ProviderId { get; set; }
    public string ProviderReference { get; set; }
    public RewardStatus Status { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string RevocationReason { get; set; }
    public string EventId { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    
    // Reward details
    public string Id { get; set; }
    public string DisplayId { get; set; }
    public string ModuleId { get; set; }
    public string ModuleName { get; set; }
}
