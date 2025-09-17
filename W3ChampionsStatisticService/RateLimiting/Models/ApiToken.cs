using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.RateLimiting.Models;

[BsonIgnoreExtraElements]
public class ApiToken : IIdentifiable
{
    [BsonId]
    public string Id { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    /// <summary>
    /// The API token (UUID format)
    /// </summary>
    public string Token { get; set; }

    /// <summary>
    /// Rate limit scopes for this token. Each scope can have different limits.
    /// Key: scope name (e.g., "replay", "stats", "profile")
    /// Value: rate limit configuration for that scope
    /// </summary>
    public Dictionary<string, ApiTokenScope> Scopes { get; set; } = new Dictionary<string, ApiTokenScope>();

    /// <summary>
    /// Whether this token is currently active
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// When the token was created
    /// </summary>
    [BsonRepresentation(BsonType.Array)]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Last time this token was used
    /// </summary>
    [BsonRepresentation(BsonType.Array)]
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>
    /// Optional expiry date for the token
    /// </summary>
    [BsonRepresentation(BsonType.Array)]
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// IP whitelist (empty means all IPs allowed)
    /// </summary>
    public string[] AllowedIPs { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Contact information for the token owner
    /// </summary>
    public string ContactDetails { get; set; }

    public ApiToken()
    {
        Id = Guid.NewGuid().ToString();
        Token = Guid.NewGuid().ToString();
        CreatedAt = DateTimeOffset.UtcNow;
        IsActive = true;
    }

    public bool IsExpired()
    {
        return ExpiresAt.HasValue && ExpiresAt.Value < DateTimeOffset.UtcNow;
    }

    public bool IsIpAllowed(string ipAddress)
    {
        if (AllowedIPs == null || AllowedIPs.Length == 0)
            return true;

        return Array.Exists(AllowedIPs, ip => ip.Equals(ipAddress, StringComparison.OrdinalIgnoreCase));
    }

    public bool HasScope(string scope)
    {
        return Scopes != null && Scopes.ContainsKey(scope);
    }

    public ApiTokenScope GetScope(string scope)
    {
        return Scopes?.GetValueOrDefault(scope);
    }
}

public class ApiTokenScope
{
    /// <summary>
    /// Hourly rate limit for this scope
    /// </summary>
    public int HourlyLimit { get; set; }

    /// <summary>
    /// Daily rate limit for this scope
    /// </summary>
    public int DailyLimit { get; set; }

    /// <summary>
    /// Whether this scope is enabled for the token
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
