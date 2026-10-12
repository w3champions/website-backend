using System;
using System.Collections.Generic;
using NUnit.Framework;
using W3C.Domain.Rewards.Entities;
using W3ChampionsStatisticService.Rewards.DTOs;

namespace WC3ChampionsStatisticService.Tests.Rewards;

[TestFixture]
public class PatreonAccountLinkDtoTests
{
    [Test]
    public void SyncedTiersOnlyIncludeActiveLocalPatreonEntitlementsForThisPlayer()
    {
        var link = new PatreonAccountLink("Player#1", "patreon-user") { LastSyncAt = DateTime.UtcNow };
        ProductMappingUserAssociation Association(string user = "Player#1", string provider = "patreon", AssociationStatus status = AssociationStatus.Active, DateTime? expiry = null) => new()
        {
            UserId = user, ProviderId = provider, ProviderProductId = "tier-gold", ProductMappingId = "mapping-gold", Status = status, ExpiresAt = expiry
        };
        var associations = new[] { Association(), Association(), Association("Other#1"), Association(provider: "kofi"), Association(status: AssociationStatus.Revoked), Association(expiry: DateTime.UtcNow.AddDays(-1)) };
        var mappings = new Dictionary<string, ProductMapping> { ["mapping-gold"] = new() { ProductName = "Gold Patreon Tier" } };
        var dto = PatreonAccountLinkDto.FromLocalState(link, associations, mappings);
        Assert.That(dto.SyncedTiers.Count, Is.EqualTo(1));
        Assert.That(dto.SyncedTiers[0].TierId, Is.EqualTo("tier-gold"));
        Assert.That(dto.SyncedTiers[0].Title, Is.EqualTo("Gold Patreon Tier"));
        Assert.That(dto.LastSyncAt, Is.EqualTo(link.LastSyncAt));
    }

    [Test]
    public void MissingMappingKeepsTheTierIdAndAnUnsyncedLinkStaysUnknown()
    {
        var link = new PatreonAccountLink("Player#1", "patreon-user");
        var associations = new[] { new ProductMappingUserAssociation { UserId = link.BattleTag, ProviderId = "patreon", ProviderProductId = "unmapped-tier", ProductMappingId = "missing", Status = AssociationStatus.Active } };
        var dto = PatreonAccountLinkDto.FromLocalState(link, associations, new Dictionary<string, ProductMapping>());
        Assert.That(dto.SyncedTiers[0].TierId, Is.EqualTo("unmapped-tier"));
        Assert.That(dto.SyncedTiers[0].Title, Is.Empty);
        Assert.That(dto.LastSyncAt, Is.Null);
        Assert.That(PatreonAccountLinkDto.FromLocalState(link, Array.Empty<ProductMappingUserAssociation>(), new Dictionary<string, ProductMapping>()).SyncedTiers, Is.Empty);
    }
}
