using System;
using System.Collections.Generic;
using System.Linq;
using W3C.Domain.Rewards.Entities;

namespace W3C.Domain.Rewards.Events;

public class RewardEvent
{
    public string EventId { get; set; }
    public RewardEventType EventType { get; set; }
    public string ProviderId { get; set; }
    public string UserId { get; set; }
    public string ProviderReference { get; set; }
    public decimal? AnnouncementAmount { get; set; } // Only for announcement purposes
    public string Currency { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();

    // All providers use this - single tier providers will have one item, multi-tier will have multiple
    public List<EntitledTier> EntitledTiers { get; set; } = new();

    /// <summary>
    /// Validates the reward event for required fields and business rules
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrEmpty(EventId))
            throw new InvalidOperationException("EventId cannot be null or empty");

        if (string.IsNullOrEmpty(ProviderId))
            throw new InvalidOperationException("ProviderId cannot be null or empty");

        if (string.IsNullOrEmpty(UserId))
            throw new InvalidOperationException("UserId cannot be null or empty");

        if (string.IsNullOrEmpty(ProviderReference))
            throw new InvalidOperationException("ProviderReference cannot be null or empty");

        if (EntitledTiers == null)
            throw new InvalidOperationException("EntitledTiers cannot be null");

        // Validate tier IDs are not empty strings
        if (EntitledTiers.Any(t => string.IsNullOrWhiteSpace(t?.TierId)))
            throw new InvalidOperationException("EntitledTiers cannot contain null or empty tier IDs");

        // Validate no duplicate tier IDs
        var tierIds = EntitledTiers.Select(t => t.TierId).ToList();
        if (tierIds.Count != tierIds.Distinct().Count())
            throw new InvalidOperationException("EntitledTiers cannot contain duplicate tier IDs");

        if (Timestamp == DateTime.MinValue || Timestamp == default)
            throw new InvalidOperationException("Timestamp must be set to a valid date");

        // Validate announcement amount if present
        if (AnnouncementAmount.HasValue && AnnouncementAmount.Value < 0)
            throw new InvalidOperationException("AnnouncementAmount cannot be negative");

        if (Metadata == null)
            throw new InvalidOperationException("Metadata cannot be null");
    }
}

public enum RewardEventType
{
    Purchase,
    SubscriptionCreated,
    SubscriptionRenewed,
    SubscriptionCancelled,
    SubscriptionExpired
}
