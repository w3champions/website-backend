using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using W3C.Domain.Rewards.Entities;

namespace W3ChampionsStatisticService.Rewards.Services;

internal static class PatreonTierFilter
{
    private const string ProviderId = "patreon";

    /// <summary>
    /// Reads REWARDS_PATREON_IGNORED_TIER_IDS env var (comma-separated tier IDs) and uses it
    /// as the ignore list. Empty/missing → no tiers ignored. The ignore list lets us drop
    /// non-monetized "follower" tiers (e.g., Patreon's free-tier feature) that have no
    /// corresponding ProductMapping, preventing infinite drift mismatch loops.
    /// </summary>
    public static List<string> Filter(List<EntitledTier> tiers, List<ProductMapping> productMappings)
    {
        return Filter(tiers, productMappings, GetIgnoredTierIdsFromEnv());
    }

    /// <summary>
    /// Test-friendly overload: ignored tier IDs supplied explicitly.
    /// Tiers in the ignore list are dropped before the passthrough/Tiered split.
    /// </summary>
    public static List<string> Filter(
        List<EntitledTier> tiers,
        List<ProductMapping> productMappings,
        IReadOnlySet<string> ignoredTierIds)
    {
        var input = (ignoredTierIds == null || ignoredTierIds.Count == 0)
            ? tiers
            : tiers.Where(t => !ignoredTierIds.Contains(t.TierId)).ToList();

        var tieredCandidates = new List<EntitledTier>();
        var passthrough = new List<string>();

        foreach (var t in input)
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
                .ThenBy(c => c.TierId, StringComparer.Ordinal)
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

    private static IReadOnlySet<string> GetIgnoredTierIdsFromEnv()
    {
        var raw = Environment.GetEnvironmentVariable("REWARDS_PATREON_IGNORED_TIER_IDS");
        if (string.IsNullOrWhiteSpace(raw)) return new HashSet<string>();
        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }
}
