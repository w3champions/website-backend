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
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ProductMapping : IIdentifiable
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProductName { get; set; }
    public List<ProductProviderPair> ProductProviders { get; set; } = new();
    public List<string> RewardIds { get; set; } = new();
    public ProductMappingType Type { get; set; }
    public Dictionary<string, object> AdditionalParameters { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ProductProviderPair
{
    public string ProviderId { get; set; }
    public string ProductId { get; set; }
    
    public ProductProviderPair() { }
    
    public ProductProviderPair(string providerId, string productId)
    {
        ProviderId = providerId;
        ProductId = productId;
    }
}

public enum ProductMappingType
{
    OneTime,
    Recurring,
    Tiered
}