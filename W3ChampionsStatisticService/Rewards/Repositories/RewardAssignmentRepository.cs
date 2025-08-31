using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using Serilog;
using W3C.Domain.Repositories;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Common.Repositories;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Common.Services;
using W3C.Domain.Rewards.ValueObjects;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Rewards.Repositories;

[Trace]
public class RewardAssignmentRepository : MongoDbRepositoryBase, IRewardAssignmentRepository
{
    private readonly IOptimisticConcurrencyService _concurrencyService;

    public RewardAssignmentRepository(MongoClient mongoClient, IOptimisticConcurrencyService concurrencyService) : base(mongoClient)
    {
        _concurrencyService = concurrencyService;
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        // try
        // {
        var collection = CreateCollection<RewardAssignment>();

        // Create unique index on EventId for webhook idempotency
        var eventIdIndex = new CreateIndexModel<RewardAssignment>(
            Builders<RewardAssignment>.IndexKeys.Ascending(x => x.EventId),
            new CreateIndexOptions
            {
                Unique = true,
                Sparse = true, // Allow null EventId but enforce uniqueness when present
                Name = "IX_EventId_Unique",
                Background = true
            });

        // Performance indexes as recommended in code review
        var userStatusProviderIndex = new CreateIndexModel<RewardAssignment>(
            Builders<RewardAssignment>.IndexKeys
                .Ascending(x => x.UserId)
                .Ascending(x => x.Status)
                .Ascending(x => x.ProviderId),
            new CreateIndexOptions { Name = "IX_UserId_Status_ProviderId_Compound", Background = true });

        var userIdIndex = new CreateIndexModel<RewardAssignment>(
            Builders<RewardAssignment>.IndexKeys.Ascending(x => x.UserId),
            new CreateIndexOptions { Name = "IX_UserId", Background = true });

        var rewardIdIndex = new CreateIndexModel<RewardAssignment>(
            Builders<RewardAssignment>.IndexKeys.Ascending(x => x.RewardId),
            new CreateIndexOptions { Name = "IX_RewardId", Background = true });

        var providerIdIndex = new CreateIndexModel<RewardAssignment>(
            Builders<RewardAssignment>.IndexKeys.Ascending(x => x.ProviderId),
            new CreateIndexOptions { Name = "IX_ProviderId", Background = true });

        var assignedAtIndex = new CreateIndexModel<RewardAssignment>(
            Builders<RewardAssignment>.IndexKeys.Descending(x => x.AssignedAt),
            new CreateIndexOptions { Name = "IX_AssignedAt_Desc", Background = true });

        collection.Indexes.CreateMany(new[] {
                eventIdIndex,
                userStatusProviderIndex,
                userIdIndex,
                rewardIdIndex,
                providerIdIndex,
                assignedAtIndex
            });
        Log.Information("Ensured performance indexes on RewardAssignment collection");
        // }
        // catch (MongoCommandException ex) when (ex.Code == 85) // IndexOptionsConflict
        // {
        //     Log.Information("EventId unique index already exists on RewardAssignment collection");
        // }
        // catch (Exception ex)
        // {
        //     Log.Fatal(ex, "Failed to create required EventId unique index on RewardAssignment collection");
        //     throw;
        // }
    }
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

    public Task<List<RewardAssignment>> GetActiveAssignmentsByProvider(string providerId)
    {
        return LoadAll<RewardAssignment>(a =>
            a.ProviderId == providerId &&
            a.Status == RewardStatus.Active);
    }

    public async Task<RewardAssignment> Create(RewardAssignment assignment)
    {
        await Insert(assignment);
        return assignment;
    }

    public async Task<RewardAssignment> Update(RewardAssignment assignment)
    {
        var collection = CreateCollection<RewardAssignment>();
        var filter = Builders<RewardAssignment>.Filter.Eq(x => x.Id, assignment.Id);

        await _concurrencyService.UpdateWithVersionAsync(collection, assignment, filter, "RewardAssignment", assignment.Id);
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

    public Task<List<RewardAssignment>> GetAll()
    {
        return LoadAll<RewardAssignment>();
    }

    public Task<List<RewardAssignment>> GetByRewardId(string rewardId)
    {
        return LoadAll<RewardAssignment>(a => a.RewardId == rewardId);
    }

    public async Task<(List<RewardAssignment> assignments, int totalCount)> GetAllPaginated(int page, int pageSize)
    {
        var collection = CreateCollection<RewardAssignment>();

        // Get total count
        var totalCount = (int)await collection.CountDocumentsAsync(FilterDefinition<RewardAssignment>.Empty);

        // Get paginated results
        var skip = (page - 1) * pageSize;
        var assignments = await collection
            .Find(FilterDefinition<RewardAssignment>.Empty)
            .Sort(Builders<RewardAssignment>.Sort.Descending(a => a.AssignedAt))
            .Skip(skip)
            .Limit(pageSize)
            .ToListAsync();

        return (assignments, totalCount);
    }
}
