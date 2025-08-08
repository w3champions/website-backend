using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.ValueObjects;

namespace W3C.Domain.Rewards.Repositories;

public interface IRewardAssignmentRepository
{
    Task<RewardAssignment> GetById(string assignmentId);
    Task<List<RewardAssignment>> GetByUserId(string userId);
    Task<List<RewardAssignment>> GetByUserIdAndStatus(string userId, RewardStatus status);
    Task<List<RewardAssignment>> GetByProviderReference(string providerId, string providerReference);
    Task<List<RewardAssignment>> GetExpiredAssignments(DateTime asOf);
    Task<RewardAssignment> Create(RewardAssignment assignment);
    Task<RewardAssignment> Update(RewardAssignment assignment);
    Task Delete(string assignmentId);
    Task<bool> ExistsForProviderReference(string providerId, string providerReference);
}