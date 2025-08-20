using System;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using Serilog;
using W3C.Domain.Repositories;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Rewards.Repositories;

[Trace]
public class PatreonAccountLinkRepository : MongoDbRepositoryBase, IPatreonAccountLinkRepository
{
    private readonly IRewardAssignmentRepository _assignmentRepo;

    public PatreonAccountLinkRepository(
        MongoClient mongoClient, 
        IRewardAssignmentRepository assignmentRepo) : base(mongoClient)
    {
        _assignmentRepo = assignmentRepo;
        EnsureIndexes();
    }
    
    private void EnsureIndexes()
    {
        try
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
            
            collection.Indexes.CreateMany(new[] { battleTagIndex, patreonUserIdIndex });
        }
        catch
        {
            // Indexes may already exist, ignore errors
        }
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

    public async Task<PatreonAccountLink> UpsertLink(string battleTag, string patreonUserId)
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
            // Update existing link
            existingByBattleTag.PatreonUserId = patreonUserId;
            existingByBattleTag.UpdateLastSync();
            
            var filter = Builders<PatreonAccountLink>.Filter.Eq(x => x.Id, existingByBattleTag.Id);
            await collection.ReplaceOneAsync(filter, existingByBattleTag);
            return existingByBattleTag;
        }
        
        // Create new link
        var newLink = new PatreonAccountLink(battleTag, patreonUserId);
        await collection.InsertOneAsync(newLink);
        
        // Handle any existing Patreon rewards that should now be associated with this BattleTag
        await HandleLinkCreation(battleTag, patreonUserId);
        
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
    /// This method logs the link creation for future reward processing
    /// </summary>
    private async Task HandleLinkCreation(string battleTag, string patreonUserId)
    {
        try
        {
            Log.Information("Patreon account link created: BattleTag {BattleTag} linked to PatreonUserId {PatreonUserId}", 
                battleTag, patreonUserId);
            
            // Future enhancement: Check if there are any pending Patreon rewards for this PatreonUserId
            // that should now be processed for the linked BattleTag
            // This would involve querying the Patreon API or checking stored webhook events
            
            await Task.CompletedTask; // Placeholder for future implementation
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling Patreon account link creation for BattleTag {BattleTag} and PatreonUserId {PatreonUserId}", 
                battleTag, patreonUserId);
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
            
            foreach (var assignment in patreonRewards)
            {
                assignment.Revoke("Patreon account unlinked");
                await _assignmentRepo.Update(assignment);
                
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
}