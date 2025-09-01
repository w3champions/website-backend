namespace W3C.Domain.Rewards.Constants;

/// <summary>
/// Constants for event types in the rewards system
/// </summary>
public static class EventTypes
{
    // Patreon event types
    public const string PatreonPledgeCreate = "members:pledge:create";
    public const string PatreonPledgeUpdate = "members:pledge:update";
    public const string PatreonPledgeDelete = "members:pledge:delete";
    public const string PatreonMemberCreate = "members:create";
    public const string PatreonMemberUpdate = "members:update";
    public const string PatreonMemberDelete = "members:delete";

    // Ko-Fi event types
    public const string KoFiDonation = "donation";
    public const string KoFiSubscription = "subscription";
    public const string KoFiShopOrder = "shop_order";

    // Internal event types
    public const string ManualAssignment = "manual_assignment";
    public const string ManualRevocation = "manual_revocation";
    public const string SystemExpiration = "system_expiration";
    public const string DriftReconciliation = "drift_reconciliation";
    public const string BulkAssignment = "bulk_assignment";
    public const string BulkRevocation = "bulk_revocation";
}
