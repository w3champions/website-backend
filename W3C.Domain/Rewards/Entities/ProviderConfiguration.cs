using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Domain.Repositories;

namespace W3C.Domain.Rewards.Entities;

public class ProviderConfiguration : IIdentifiable
{
    [BsonId]
    public string Id { get; set; }
    public string ProviderId { get; set; }
    public string ProviderName { get; set; }
    public bool IsActive { get; set; } = true;
    public Dictionary<string, string> Settings { get; set; } = new();
    public List<ProductMapping> ProductMappings { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ProductMapping
{
    public string ProviderProductId { get; set; }
    public string ProviderProductName { get; set; }
    public string RewardId { get; set; }
    public ProductMappingType Type { get; set; }
    public Dictionary<string, object> AdditionalParameters { get; set; } = new();
}

public enum ProductMappingType
{
    OneTime,
    Recurring,
    Tiered
}