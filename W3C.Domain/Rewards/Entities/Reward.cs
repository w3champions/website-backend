using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Domain.Repositories;
using W3C.Domain.Rewards.ValueObjects;

namespace W3C.Domain.Rewards.Entities;

public class Reward : IIdentifiable
{
    [BsonId]
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public RewardType Type { get; set; }
    public string ModuleId { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
    public RewardDuration Duration { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    public bool IsPermanent() => Duration?.Type == DurationType.Permanent;
    
    public DateTime? CalculateExpirationDate(DateTime fromDate)
    {
        if (Duration == null || Duration.Type == DurationType.Permanent)
            return null;
            
        return Duration.Type switch
        {
            DurationType.Days => fromDate.AddDays(Duration.Value),
            DurationType.Months => fromDate.AddMonths(Duration.Value),
            DurationType.Years => fromDate.AddYears(Duration.Value),
            _ => null
        };
    }
}

public enum RewardType
{
    Portrait,
    Badge,
    Title,
    Cosmetic,
    Feature,
    Other
}