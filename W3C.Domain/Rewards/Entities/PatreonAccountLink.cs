using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Domain.Repositories;

namespace W3C.Domain.Rewards.Entities;

public class PatreonAccountLink : IIdentifiable
{
    [BsonId]
    public ObjectId Id { get; set; }

    // IIdentifiable requires string Id, so we provide it
    string IIdentifiable.Id => Id.ToString();

    /// <summary>
    /// Battle.net BattleTag (from JWT token)
    /// </summary>
    public string BattleTag { get; set; }

    /// <summary>
    /// Patreon user ID (from Patreon API)
    /// </summary>
    public string PatreonUserId { get; set; }

    /// <summary>
    /// When the account link was created
    /// </summary>
    public DateTime LinkedAt { get; set; }

    /// <summary>
    /// Last time this link was used for reward sync
    /// </summary>
    public DateTime? LastSyncAt { get; set; }

    public PatreonAccountLink()
    {
        LinkedAt = DateTime.UtcNow;
    }

    public PatreonAccountLink(string battleTag, string patreonUserId) : this()
    {
        BattleTag = battleTag ?? throw new ArgumentNullException(nameof(battleTag));
        PatreonUserId = patreonUserId ?? throw new ArgumentNullException(nameof(patreonUserId));
    }

    public void UpdateLastSync()
    {
        LastSyncAt = DateTime.UtcNow;
    }
}
