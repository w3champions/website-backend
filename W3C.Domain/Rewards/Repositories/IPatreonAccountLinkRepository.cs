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

    /// <summary>
    /// Update an existing account link in-place (e.g. to refresh LastSyncAt)
    /// </summary>
    Task Update(PatreonAccountLink link);

    /// <summary>
    /// Refreshes the LastSyncAt timestamp on the link matching this battleTag, if one exists.
    /// No-op if no link exists. Failures are caught + logged internally — never throws to caller.
    /// </summary>
    Task RefreshLastSyncAt(string battleTag);

    /// <summary>
    /// Delete account link by ID
    /// </summary>
    Task Delete(string id);

    /// <summary>
    /// Delete account link by ObjectId
    /// </summary>
    Task Delete(MongoDB.Bson.ObjectId id);
}
