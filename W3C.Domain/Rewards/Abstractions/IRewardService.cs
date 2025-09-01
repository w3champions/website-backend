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
    Task<RewardAssignment> AssignRewardWithEventId(string userId, string rewardId, string providerId, string providerReference, string eventId);
    Task RevokeReward(string assignmentId);
    Task RevokeReward(string assignmentId, string reason);
    Task ExpireReward(string assignmentId);
    Task<List<RewardAssignment>> GetExpiredRewards();
    Task ProcessExpiredRewards();
}
