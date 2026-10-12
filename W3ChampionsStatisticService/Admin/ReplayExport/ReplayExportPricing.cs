using System;

namespace W3ChampionsStatisticService.Admin.ReplayExport;

/// <summary>
/// The price of a replay download. Admins download for free on the admin page; buyers pay it in the replay
/// store. The history keeps the price of each download.
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

    /// <summary>True when the user does not pay: the admin page.</summary>
    public bool Free { get; set; }

    /// <summary>False when the replay store cannot take payments (no payment provider is set up).</summary>
    public bool Available { get; set; } = true;
}
