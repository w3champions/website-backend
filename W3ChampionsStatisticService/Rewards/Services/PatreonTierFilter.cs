using System.Collections.Generic;
using System.Linq;
using Serilog;
using W3C.Domain.Rewards.Entities;

namespace W3ChampionsStatisticService.Rewards.Services;

internal static class PatreonTierFilter
{
    private const string ProviderId = "patreon";

    public static List<string> Filter(List<EntitledTier> tiers, List<ProductMapping> productMappings)
    {
        var tieredCandidates = new List<EntitledTier>();
        var passthrough = new List<string>();

        foreach (var t in tiers)
        {
            var mapping = productMappings.FirstOrDefault(pm =>
                pm.ProductProviders.Any(pp => pp.ProviderId == ProviderId && pp.ProductId == t.TierId));
            if (mapping?.Type == ProductMappingType.Tiered)
                tieredCandidates.Add(t);
            else
                passthrough.Add(t.TierId);
        }

        var result = new List<string>(passthrough);
        if (tieredCandidates.Any())
        {
            var winner = tieredCandidates
                .OrderByDescending(c => c.AmountCents.HasValue && c.AmountCents.Value >= 0 ? c.AmountCents.Value : long.MinValue)
                .ThenBy(c => c.TierId, System.StringComparer.Ordinal)
                .First();
            result.Add(winner.TierId);
            if (tieredCandidates.Count > 1)
                Log.Information("Tier upgrade: keeping highest-priced {Winner} ({Cents}c) over {Others}",
                    winner.TierId, winner.AmountCents,
                    string.Join(",", tieredCandidates.Where(c => c.TierId != winner.TierId)
                        .Select(c => $"{c.TierId}({c.AmountCents}c)")));
        }
        return result;
    }
}
