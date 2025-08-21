using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Domain.Repositories;

namespace W3C.Domain.Rewards.Entities;

public class ProviderConfiguration : IIdentifiable
{
    [BsonId]
    public string Id { get; set; }
    public string ProviderId { get; set; }
    public List<ProductMapping> ProductMappings { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ProductMapping
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public List<string> ProviderProductIds { get; set; } = new();
    public string ProviderProductName { get; set; }
    public List<string> RewardIds { get; set; } = new();
    public ProductMappingType Type { get; set; }
    public Dictionary<string, object> AdditionalParameters { get; set; } = new();
}

public enum ProductMappingType
{
    OneTime,
    Recurring,
    Tiered
}