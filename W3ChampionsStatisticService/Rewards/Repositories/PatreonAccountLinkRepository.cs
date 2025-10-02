using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using W3C.Domain.Repositories;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Rewards.Services;

namespace W3ChampionsStatisticService.Rewards.Repositories;

[Trace]
public class PatreonAccountLinkRepository(
    MongoClient mongoClient,
    IRewardAssignmentRepository assignmentRepo,
    IServiceProvider serviceProvider) : MongoDbRepositoryBase(mongoClient), IPatreonAccountLinkRepository, IRequiresIndexes
{
    private readonly IRewardAssignmentRepository _assignmentRepo = assignmentRepo;
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public string CollectionName => "PatreonAccountLink";

    public async Task EnsureIndexesAsync()
    {
        var collection = CreateCollection<PatreonAccountLink>();

        // Create unique index on BattleTag
        var battleTagIndex = new CreateIndexModel<PatreonAccountLink>(
            Builders<PatreonAccountLink>.IndexKeys.Ascending(x => x.BattleTag),
            new CreateIndexOptions { Unique = true });

        // Create unique index on PatreonUserId
        var patreonUserIdIndex = new CreateIndexModel<PatreonAccountLink>(
            Builders<PatreonAccountLink>.IndexKeys.Ascending(x => x.PatreonUserId),
            new CreateIndexOptions { Unique = true });

        await collection.Indexes.CreateManyAsync(new[] { battleTagIndex, patreonUserIdIndex });
    }

    public async Task<PatreonAccountLink> GetByBattleTag(string battleTag)
    {
        var collection = CreateCollection<PatreonAccountLink>();
        var filter = Builders<PatreonAccountLink>.Filter.Eq(x => x.BattleTag, battleTag);
        return await collection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<PatreonAccountLink> GetByPatreonUserId(string patreonUserId)
    {
        var collection = CreateCollection<PatreonAccountLink>();
        var filter = Builders<PatreonAccountLink>.Filter.Eq(x => x.PatreonUserId, patreonUserId);
        return await collection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<PatreonAccountLink> UpsertLink(string battleTag, string patreonUserId, string accessToken = null)
    {
        var collection = CreateCollection<PatreonAccountLink>();

        // Check if Patreon account is already linked to a different BattleTag
        var existingByPatreon = await GetByPatreonUserId(patreonUserId);
        if (existingByPatreon != null && existingByPatreon.BattleTag != battleTag)
        {
            // Remove the old link - this will trigger reward removal notification
            await RemoveByBattleTag(existingByPatreon.BattleTag);
        }

        // Try to find existing link by BattleTag
        var existingByBattleTag = await GetByBattleTag(battleTag);
        if (existingByBattleTag != null)
        {
            // If linking to the same PatreonUserId, just update the existing link
            if (existingByBattleTag.PatreonUserId == patreonUserId)
            {
                existingByBattleTag.UpdateLastSync();

                var filter = Builders<PatreonAccountLink>.Filter.Eq(x => x.Id, existingByBattleTag.Id);
                await collection.ReplaceOneAsync(filter, existingByBattleTag);

                // Trigger sync if access token is available
                await HandleLinkCreation(battleTag, patreonUserId, accessToken);

                return existingByBattleTag;
            }
            else
            {
                // Different PatreonUserId - remove the old link to trigger cleanup
                await RemoveByBattleTag(existingByBattleTag.BattleTag);
            }
        }

        // Create new link
        var newLink = new PatreonAccountLink(battleTag, patreonUserId);
        await collection.InsertOneAsync(newLink);

        // Handle any existing Patreon rewards that should now be associated with this BattleTag
        await HandleLinkCreation(battleTag, patreonUserId, accessToken);

        return newLink;
    }

    public async Task<bool> RemoveByBattleTag(string battleTag)
    {
        var collection = CreateCollection<PatreonAccountLink>();

        // Get the link before removing it so we can notify the reward system
        var existingLink = await GetByBattleTag(battleTag);
        if (existingLink == null)
        {
            return false; // Nothing to remove
        }

        var filter = Builders<PatreonAccountLink>.Filter.Eq(x => x.BattleTag, battleTag);
        var result = await collection.DeleteOneAsync(filter);

        if (result.DeletedCount > 0)
        {
            // Handle removal of all Patreon rewards for this BattleTag
            await HandleLinkRemoval(battleTag, existingLink.PatreonUserId);
        }

        return result.DeletedCount > 0;
    }

    /// <summary>
    /// Handle the creation of a new Patreon account link
    /// Triggers immediate reward sync if access token is available
    /// </summary>
    private async Task HandleLinkCreation(string battleTag, string patreonUserId, string accessToken = null)
    {
        try
        {
            Log.Information("Patreon account link created: BattleTag {BattleTag} linked to PatreonUserId {PatreonUserId}",
                battleTag, patreonUserId);

            // Attempt immediate sync if access token is available
            if (!string.IsNullOrEmpty(accessToken))
            {
                Log.Information("Access token available - triggering immediate reward sync for BattleTag {BattleTag}", battleTag);

                var driftDetectionService = _serviceProvider.GetRequiredService<PatreonDriftDetectionService>();
                var syncResult = await driftDetectionService.SyncSingleUser(battleTag, patreonUserId, accessToken);

                if (syncResult.Success)
                {
                    Log.Information("Successfully synced rewards for newly linked BattleTag {BattleTag}. Action: {SyncAction}, Message: {Message}",
                        battleTag, syncResult.SyncAction, syncResult.Message);
                }
                else
                {
                    Log.Warning("Failed to sync rewards for newly linked BattleTag {BattleTag}. Error: {Error}",
                        battleTag, syncResult.ErrorMessage);
                }
            }
            else
            {
                Log.Information("No access token available - user rewards will be synced during next drift detection cycle");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling Patreon account link creation for BattleTag {BattleTag} and PatreonUserId {PatreonUserId}",
                battleTag, patreonUserId);
            // Don't throw - link creation should succeed even if sync fails
        }
    }

    /// <summary>
    /// Handle the removal of a Patreon account link
    /// This method revokes all Patreon rewards for the BattleTag
    /// </summary>
    private async Task HandleLinkRemoval(string battleTag, string patreonUserId)
    {
        try
        {
            Log.Information("Patreon account link removed: BattleTag {BattleTag} unlinked from PatreonUserId {PatreonUserId}",
                battleTag, patreonUserId);

            // Find all Patreon rewards for this BattleTag and revoke them
            var patreonAssignments = await _assignmentRepo.GetByUserIdAndStatus(battleTag, RewardStatus.Active);
            var patreonRewards = patreonAssignments.Where(a => a.ProviderId == "patreon").ToList();

            var rewardService = _serviceProvider.GetRequiredService<IRewardService>();

            foreach (var assignment in patreonRewards)
            {
                await rewardService.RevokeReward(assignment.Id, "Patreon account unlinked");

                Log.Information("Revoked Patreon reward {RewardId} for BattleTag {BattleTag}",
                    assignment.RewardId, battleTag);
            }

            Log.Information("Revoked {Count} Patreon rewards for unlinked BattleTag {BattleTag}",
                patreonRewards.Count, battleTag);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling Patreon account link removal for BattleTag {BattleTag} and PatreonUserId {PatreonUserId}",
                battleTag, patreonUserId);
        }
    }

    public Task<List<PatreonAccountLink>> GetAll()
    {
        return LoadAll<PatreonAccountLink>();
    }

    public Task Delete(string id)
    {
        var objectId = ObjectId.Parse(id);
        return Delete<PatreonAccountLink>(link => link.Id == objectId);
    }

    public Task Delete(ObjectId id)
    {
        return Delete<PatreonAccountLink>(link => link.Id == id);
    }
}
