using System;
using System.Text.Json.Serialization;

namespace W3ChampionsStatisticService.Rewards.Providers.Patreon;

public class PatreonWebhookData
{
    [JsonPropertyName("data")]
    public PatreonData Data { get; set; }
}

public class PatreonData
{
    [JsonPropertyName("id")]
    public string Id { get; set; }
    
    [JsonPropertyName("type")]
    public string Type { get; set; }
    
    [JsonPropertyName("attributes")]
    public PatreonAttributes Attributes { get; set; }
    
    [JsonPropertyName("relationships")]
    public PatreonRelationships Relationships { get; set; }
}

public class PatreonAttributes
{
    [JsonPropertyName("patron_status")]
    public string PatronStatus { get; set; }
    
    [JsonPropertyName("email")]
    public string Email { get; set; }
}

public class PatreonRelationships
{
    [JsonPropertyName("user")]
    public PatreonRelationship User { get; set; }
    
    [JsonPropertyName("currently_entitled_tiers")]
    public PatreonTiersRelationship CurrentlyEntitledTiers { get; set; }
}

public class PatreonRelationship
{
    [JsonPropertyName("data")]
    public PatreonRelationshipData Data { get; set; }
}

public class PatreonRelationshipData
{
    [JsonPropertyName("id")]
    public string Id { get; set; }
    
    [JsonPropertyName("type")]
    public string Type { get; set; }
}

public class PatreonTiersRelationship
{
    [JsonPropertyName("data")]
    public PatreonRelationshipData[] Data { get; set; }
}