using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Rewards.Repositories;

[Trace]
public class RewardAssignmentRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IRewardAssignmentRepository
{
    public Task<RewardAssignment> GetById(string assignmentId)
    {
        return LoadFirst<RewardAssignment>(assignmentId);
    }

    public Task<List<RewardAssignment>> GetByUserId(string userId)
    {
        return LoadAll<RewardAssignment>(a => a.UserId == userId);
    }

    public Task<List<RewardAssignment>> GetByUserIdAndStatus(string userId, RewardStatus status)
    {
        return LoadAll<RewardAssignment>(a => a.UserId == userId && a.Status == status);
    }

    public Task<List<RewardAssignment>> GetByProviderReference(string providerId, string providerReference)
    {
        return LoadAll<RewardAssignment>(a => 
            a.ProviderId == providerId && 
            a.ProviderReference == providerReference);
    }

    public Task<List<RewardAssignment>> GetExpiredAssignments(DateTime asOf)
    {
        return LoadAll<RewardAssignment>(a => 
            a.Status == RewardStatus.Active && 
            a.ExpiresAt != null && 
            a.ExpiresAt <= asOf);
    }

    public async Task<RewardAssignment> Create(RewardAssignment assignment)
    {
        await Insert(assignment);
        return assignment;
    }

    public async Task<RewardAssignment> Update(RewardAssignment assignment)
    {
        await Upsert(assignment);
        return assignment;
    }

    public Task Delete(string assignmentId)
    {
        return Delete<RewardAssignment>(assignmentId);
    }

    public async Task<bool> ExistsForProviderReference(string providerId, string providerReference)
    {
        var existing = await LoadFirst<RewardAssignment>(a => 
            a.ProviderId == providerId && 
            a.ProviderReference == providerReference);
        return existing != null;
    }
}