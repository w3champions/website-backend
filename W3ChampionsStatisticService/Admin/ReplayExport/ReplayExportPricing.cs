using System;

namespace W3ChampionsStatisticService.Admin.ReplayExport;

/// <summary>
/// The list price of a replay download. Nobody pays yet: only admins can download, and admins download for
/// free. The page shows the price, and the history keeps it, so a public paid download can start from here.
/// </summary>
public static class ReplayExportPricing
{
    public const string Currency = "USD";
    public const decimal PricePerReplay = 0.02m;
    public const decimal MinimumPrice = 5.00m;

    /// <summary>The price of <paramref name="replays"/> replays: the price per replay, but at least the minimum. No replays cost nothing.</summary>
    public static decimal Price(int replays) =>
        replays <= 0 ? 0 : Math.Max(Math.Round(replays * PricePerReplay, 2, MidpointRounding.AwayFromZero), MinimumPrice);
}

public class ReplayExportPrices
{
    public string Currency { get; set; } = ReplayExportPricing.Currency;
    public decimal PricePerReplay { get; set; } = ReplayExportPricing.PricePerReplay;
    public decimal MinimumPrice { get; set; } = ReplayExportPricing.MinimumPrice;

    /// <summary>True when the user does not pay. Today that is every user: the page is for admins only.</summary>
    public bool Free { get; set; } = true;
}
