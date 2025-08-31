using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace W3C.Domain.Common.Entities;

/// <summary>
/// Represents an audit log entry for system actions
/// </summary>
public class AuditLogEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// BattleTag of the admin who performed the action
    /// </summary>
    public string AdminBattleTag { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the action occurred
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Action that was performed (e.g., "CREATE", "UPDATE", "DELETE", "ASSIGN", "REVOKE")
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Category of the audit log (e.g., "REWARD", "ASSIGNMENT", "PROVIDER", "DRIFT", "USER", "CLAN")
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Type of entity being modified (e.g., "Reward", "RewardAssignment", "ProductMapping", "User", "Clan")
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// ID of the entity being modified
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// User ID affected by this action (if applicable)
    /// </summary>
    public string? AffectedUserId { get; set; }

    /// <summary>
    /// Provider ID (if applicable)
    /// </summary>
    public string? ProviderId { get; set; }

    /// <summary>
    /// Reason for the action (especially for revocations)
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Previous value before the change (serialized as JSON)
    /// </summary>
    public string? OldValue { get; set; }

    /// <summary>
    /// New value after the change (serialized as JSON)
    /// </summary>
    public string? NewValue { get; set; }

    /// <summary>
    /// Additional metadata related to the action
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// IP address of the admin (if available)
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent of the admin (if available)
    /// </summary>
    public string? UserAgent { get; set; }
}
