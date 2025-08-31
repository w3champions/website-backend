using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Domain.Repositories;
using W3C.Domain.Common.Services;
using W3C.Domain.Rewards.ValueObjects;

namespace W3C.Domain.Rewards.Entities;

public class RewardAssignment : IIdentifiable, IVersioned
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; }
    public string RewardId { get; set; }
    public string ProviderId { get; set; }
    public string ProviderReference { get; set; }
    public string EventId { get; set; } // For idempotency - prevents duplicate processing
    public RewardStatus Status { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string RevokedReason { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Version field for optimistic concurrency control
    /// </summary>
    [BsonElement("_version")]
    public long Version { get; set; } = 0;

    public bool IsActive() => Status == RewardStatus.Active && !IsExpired();
    public bool IsExpired() => ExpiresAt.HasValue && ExpiresAt.Value <= DateTime.UtcNow;

    public void Expire()
    {
        Status = RewardStatus.Expired;
    }

    public void Revoke(string reason = null)
    {
        Status = RewardStatus.Revoked;
        RevokedAt = DateTime.UtcNow;
        RevokedReason = reason;
    }

    public void Refresh(DateTime newExpirationDate)
    {
        if (Status == RewardStatus.Expired)
        {
            Status = RewardStatus.Active;
        }
        ExpiresAt = newExpirationDate;
    }
}

public class RewardAssignmentRequest
{
    public string UserId { get; set; }
    public string RewardId { get; set; }
    public string ProviderId { get; set; }
    public string ProviderReference { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
