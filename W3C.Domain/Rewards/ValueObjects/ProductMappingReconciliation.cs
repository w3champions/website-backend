using System;
using System.Collections.Generic;

namespace W3C.Domain.Rewards.ValueObjects;

public class ProductMappingReconciliationResult
{
    public string ProductMappingId { get; set; }
    public string ProductMappingName { get; set; }
    public DateTime ReconciliationTimestamp { get; set; }
    public List<string> AddedRewardIds { get; set; } = new();
    public List<string> RemovedRewardIds { get; set; } = new();
    public List<UserReconciliationEntry> UserReconciliations { get; set; } = new();
    public int TotalUsersAffected { get; set; }
    public int RewardsAdded { get; set; }
    public int RewardsRevoked { get; set; }
    public bool Success { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool WasDryRun { get; set; }
}

public class UserReconciliationEntry
{
    public string UserId { get; set; }
    public string ProductMappingId { get; set; }
    public string ProductMappingName { get; set; }
    public List<ReconciliationAction> Actions { get; set; } = new();
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
}

public class ReconciliationAction
{
    public string RewardId { get; set; }
    public ReconciliationActionType Type { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public string AssignmentId { get; set; } // For tracking revoked assignments
}

public enum ReconciliationActionType
{
    Added,
    Removed
}
