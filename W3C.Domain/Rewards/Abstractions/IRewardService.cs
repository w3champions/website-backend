using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Events;

namespace W3C.Domain.Rewards.Abstractions;

public interface IRewardService
{
    Task<RewardAssignment> ProcessRewardEvent(RewardEvent rewardEvent);
    Task<List<RewardAssignment>> GetUserRewards(string userId);
    Task<RewardAssignment> AssignReward(string userId, string rewardId, string providerId, string providerReference);
    Task RevokeReward(string assignmentId);
    Task RevokeReward(string assignmentId, string reason);
    Task ExpireReward(string assignmentId);
    Task<List<RewardAssignment>> GetExpiredRewards();
    Task ProcessExpiredRewards();
}

public interface IRewardAssignmentService
{
    Task<RewardAssignment> CreateAssignment(RewardAssignmentRequest request);
    Task<bool> RefreshAssignment(string assignmentId);
    Task<bool> IsEligibleForReward(string userId, string rewardId);
    Task<List<RewardAssignment>> GetActiveAssignments(string userId);
    Task<List<RewardAssignment>> GetAssignmentsByProvider(string userId, string providerId);
}