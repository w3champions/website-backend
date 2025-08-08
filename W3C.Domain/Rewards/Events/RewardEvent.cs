using System;
using System.Collections.Generic;

namespace W3C.Domain.Rewards.Events;

public class RewardEvent
{
    public string EventId { get; set; }
    public RewardEventType EventType { get; set; }
    public string ProviderId { get; set; }
    public string UserId { get; set; }
    public string ProductId { get; set; }
    public string ProviderReference { get; set; }
    public decimal? AnnouncementAmount { get; set; } // Only for announcement purposes
    public string Currency { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public enum RewardEventType
{
    Purchase,
    SubscriptionCreated,
    SubscriptionRenewed,
    SubscriptionCancelled,
    SubscriptionExpired
}