using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Domain.Repositories;
using W3C.Domain.Common.Services;
using MongoDB.Bson;

namespace W3C.Domain.Rewards.Entities;

/// <summary>
/// Represents a direct association between a product mapping and a user,
/// indicating that the user is entitled to rewards through this specific product mapping.
/// </summary>
public class ProductMappingUserAssociation : IIdentifiable, IVersioned
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The product mapping this user is associated with
    /// </summary>
    public string ProductMappingId { get; set; } = string.Empty;

    /// <summary>
    /// The user (BattleTag) who has access to this product mapping
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The provider through which this association was established (e.g., "patreon", "kofi")
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// The specific provider product ID that grants this entitlement
    /// </summary>
    public string ProviderProductId { get; set; } = string.Empty;

    /// <summary>
    /// When this association was first established
    /// </summary>
    public DateTime AssignedAt { get; set; }

    /// <summary>
    /// When this association was last updated (e.g., subscription renewed)
    /// </summary>
    public DateTime? LastUpdatedAt { get; set; }

    /// <summary>
    /// Current status of this association
    /// </summary>
    public AssociationStatus Status { get; set; }

    /// <summary>
    /// Optional expiration date for time-limited associations
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Additional metadata about this association (e.g., original event ID, provider reference)
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Version field for optimistic concurrency control
    /// </summary>
    [BsonElement("_version")]
    public long Version { get; set; } = 0;

    /// <summary>
    /// Checks if this association is currently active
    /// </summary>
    /// <returns>True if active and not expired</returns>
    public bool IsActive()
    {
        return Status == AssociationStatus.Active &&
               (ExpiresAt == null || ExpiresAt > DateTime.UtcNow);
    }

    /// <summary>
    /// Marks this association as revoked
    /// </summary>
    /// <param name="reason">Reason for revocation</param>
    public void Revoke(string reason)
    {
        Status = AssociationStatus.Revoked;
        LastUpdatedAt = DateTime.UtcNow;
        Metadata["revocation_reason"] = reason;
    }

    /// <summary>
    /// Marks this association as expired
    /// </summary>
    public void Expire()
    {
        Status = AssociationStatus.Expired;
        LastUpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Refreshes this association with a new expiration date
    /// </summary>
    /// <param name="newExpirationDate">New expiration date, or null for permanent</param>
    public void Refresh(DateTime? newExpirationDate)
    {
        Status = AssociationStatus.Active;
        ExpiresAt = newExpirationDate;
        LastUpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// Status of a product mapping user association
/// </summary>
public enum AssociationStatus
{
    /// <summary>
    /// Association is active and user has access
    /// </summary>
    Active = 0,

    /// <summary>
    /// Association was revoked (e.g., subscription cancelled)
    /// </summary>
    Revoked = 1,

    /// <summary>
    /// Association expired naturally
    /// </summary>
    Expired = 2
}
