using System;
using System.Collections.Generic;
using System.Linq;
using W3C.Domain.Rewards.Entities;

namespace W3ChampionsStatisticService.Rewards.DTOs;

public class PatreonAccountLinkDto
{
    public string Id { get; set; }
    public string BattleTag { get; set; }
    public string PatreonUserId { get; set; }
    public DateTime LinkedAt { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    public List<PatreonSyncedTierDto> SyncedTiers { get; set; } = new();

    internal static PatreonAccountLinkDto FromLocalState(PatreonAccountLink link,
        IEnumerable<ProductMappingUserAssociation> associations, IReadOnlyDictionary<string, ProductMapping> mappings)
    {
        var tiers = associations.Where(a => a.UserId == link.BattleTag && a.ProviderId == "patreon" && a.IsActive() && !string.IsNullOrWhiteSpace(a.ProviderProductId))
            .GroupBy(a => a.ProviderProductId)
            .Select(group => new PatreonSyncedTierDto
            {
                TierId = group.Key,
                Title = string.Join(" / ", group.Select(a => mappings.TryGetValue(a.ProductMappingId, out var mapping) ? mapping.ProductName : null)
                    .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().OrderBy(name => name))
            }).OrderBy(tier => tier.Title).ToList();
        return new PatreonAccountLinkDto
        {
            Id = link.Id.ToString(), BattleTag = link.BattleTag, PatreonUserId = link.PatreonUserId,
            LinkedAt = link.LinkedAt, LastSyncAt = link.LastSyncAt, Metadata = link.Metadata, SyncedTiers = tiers
        };
    }
}

public class PatreonSyncedTierDto
{
    public string TierId { get; set; }
    public string Title { get; set; }
}
