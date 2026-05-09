using System.Collections.Generic;
using NUnit.Framework;
using W3C.Domain.Rewards.Entities;
using W3ChampionsStatisticService.Rewards.Services;

namespace WC3ChampionsStatisticService.Tests.Rewards;

[TestFixture]
public class PatreonTierFilterTests
{
    [Test]
    public void Filter_WithIgnoredTier_DropsBeforeProcessing()
    {
        var tiers = new List<EntitledTier>
        {
            new() { TierId = "15145463", AmountCents = 0 },
            new() { TierId = "6482092", AmountCents = 10000 } // Grand Master tiered
        };
        var mappings = new List<ProductMapping>
        {
            new() {
                Id = "m-grandmaster",
                Type = ProductMappingType.Tiered,
                ProductProviders = new List<ProductProviderPair> {
                    new() { ProviderId = "patreon", ProductId = "6482092" }
                }
            }
        };

        var ignored = new HashSet<string> { "15145463" };
        var result = PatreonTierFilter.Filter(tiers, mappings, ignored);

        Assert.That(result, Is.EquivalentTo(new[] { "6482092" }));
    }

    [Test]
    public void Filter_WithoutIgnoreList_PassesUnmappedTierThrough()
    {
        var tiers = new List<EntitledTier>
        {
            new() { TierId = "15145463", AmountCents = 0 },
            new() { TierId = "6482092", AmountCents = 10000 }
        };
        var mappings = new List<ProductMapping>
        {
            new() {
                Id = "m-grandmaster",
                Type = ProductMappingType.Tiered,
                ProductProviders = new List<ProductProviderPair> { new() { ProviderId = "patreon", ProductId = "6482092" } }
            }
        };

        var result = PatreonTierFilter.Filter(tiers, mappings, ignoredTierIds: null);

        Assert.That(result, Is.EquivalentTo(new[] { "15145463", "6482092" }));
    }

    [Test]
    public void Filter_WithEmptyIgnoreList_DoesNotDropTiers()
    {
        var tiers = new List<EntitledTier>
        {
            new() { TierId = "15145463", AmountCents = 0 }
        };
        var mappings = new List<ProductMapping>();
        var empty = new HashSet<string>();

        var result = PatreonTierFilter.Filter(tiers, mappings, empty);

        Assert.That(result, Is.EquivalentTo(new[] { "15145463" }));
    }
}
