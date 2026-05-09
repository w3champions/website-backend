namespace W3C.Domain.Rewards.Entities;

/// <summary>
/// Internal Patreon/KoFi tier representation. Carries TierId for routing,
/// AmountCents for the tier-filter ranking, and Title for human-readable
/// display.
/// </summary>
/// <remarks>
/// Wire format: external API responses currently project to
/// <c>entitledTierIds: List&lt;string&gt;</c> via <c>.Select(t => t.TierId)</c>
/// at the controller boundary to preserve compatibility with admin
/// frontend tooling. Do not serialize this type directly to public APIs
/// without intentional coordination.
/// </remarks>
public class EntitledTier
{
    public string TierId { get; set; }
    public long? AmountCents { get; set; }
    public string Title { get; set; }
}
