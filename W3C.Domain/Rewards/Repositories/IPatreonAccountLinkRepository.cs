using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Rewards.Entities;

namespace W3C.Domain.Rewards.Repositories;

public interface IPatreonAccountLinkRepository
{
    /// <summary>
    /// Get account link by Battle.net BattleTag
    /// </summary>
    Task<PatreonAccountLink> GetByBattleTag(string battleTag);
    
    /// <summary>
    /// Get account link by Patreon user ID
    /// </summary>
    Task<PatreonAccountLink> GetByPatreonUserId(string patreonUserId);
    
    /// <summary>
    /// Create or update account link
    /// </summary>
    Task<PatreonAccountLink> UpsertLink(string battleTag, string patreonUserId, string accessToken = null);
    
    /// <summary>
    /// Remove account link by Battle.net BattleTag
    /// </summary>
    Task<bool> RemoveByBattleTag(string battleTag);
    
    /// <summary>
    /// Get all Patreon account links
    /// </summary>
    Task<List<PatreonAccountLink>> GetAll();
}