namespace W3C.Domain.Rewards.Entities;

/// <summary>
/// Internal Patreon/KoFi tier representation. Carries TierId for routing,
/// AmountCents for the tier-filter ranking, and Title for human-readable
/// display.
/// </summary>
/// <remarks>
/// Serialized directly on the wire by AdminRewardController.GetPatreonMemberDetails,
/// PatreonWebhookController webhook ack, and RewardDriftDetectionController
/// missing-members payloads as <c>entitledTiers: [{tierId, amountCents, title}]</c>
/// (camelCase via System.Text.Json defaults). The admin frontend consumes all three
/// fields to render readable titles plus a tooltip with the tier ID and amount.
/// Preserve this shape when modifying the type.
/// </remarks>
public class EntitledTier
{
    public string TierId { get; set; }
    public long? AmountCents { get; set; }
    public string Title { get; set; }
}
