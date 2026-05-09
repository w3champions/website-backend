using System.Collections.Generic;
using NUnit.Framework;
using W3C.Domain.Rewards.Entities;
using W3ChampionsStatisticService.Rewards.Services;
using WC3ChampionsStatisticService.UnitTests.Rewards.Builders;

namespace WC3ChampionsStatisticService.UnitTests.Rewards;

[TestFixture]
public class FilterTierIdsForProcessingTests
{
    private List<ProductMapping> _mappings;

    [SetUp]
    public void SetUp()
    {
        _mappings = new List<ProductMapping>
        {
            // Bronze, Silver, Gold all Tiered. Distinct tier IDs.
            new ProductMapping { Type = ProductMappingType.Tiered, ProductProviders = new() { new ProductProviderPair { ProviderId = "patreon", ProductId = "6482051" } } },
            new ProductMapping { Type = ProductMappingType.Tiered, ProductProviders = new() { new ProductProviderPair { ProviderId = "patreon", ProductId = "6482057" } } },
            new ProductMapping { Type = ProductMappingType.Tiered, ProductProviders = new() { new ProductProviderPair { ProviderId = "patreon", ProductId = "6482070" } } },
            // A non-Tiered mapping for additive tests
            new ProductMapping { Type = ProductMappingType.OneTime, ProductProviders = new() { new ProductProviderPair { ProviderId = "patreon", ProductId = "onetime-1" } } }
        };
    }

    [Test]
    public void TwoTieredMappings_PicksHighestAmountCents()
    {
        var input = new List<EntitledTier> { EntitledTierBuilder.Bronze(), EntitledTierBuilder.Gold() };
        var result = PatreonTierFilter.Filter(input, _mappings);
        Assert.AreEqual(new[] { "6482070" }, result.ToArray());
    }

    [Test]
    public void HighestAmountIsLastInList_PicksLast()
    {
        var input = new List<EntitledTier> { EntitledTierBuilder.Bronze(), EntitledTierBuilder.Silver(), EntitledTierBuilder.Gold() };
        var result = PatreonTierFilter.Filter(input, _mappings);
        Assert.AreEqual(new[] { "6482070" }, result.ToArray(), "Defeats .First() regression: must pick last (Gold) by amount, not first (Bronze) by position.");
    }

    [Test]
    public void TiedAmountCents_TiebreakerByTierIdOrdinal()
    {
        var t1 = new EntitledTierBuilder().WithTierId("Z-tier").WithAmountCents(500).Build();
        var t2 = new EntitledTierBuilder().WithTierId("A-tier").WithAmountCents(500).Build();
        var mappings = new List<ProductMapping>
        {
            new ProductMapping { Type = ProductMappingType.Tiered, ProductProviders = new() { new ProductProviderPair { ProviderId = "patreon", ProductId = "Z-tier" } } },
            new ProductMapping { Type = ProductMappingType.Tiered, ProductProviders = new() { new ProductProviderPair { ProviderId = "patreon", ProductId = "A-tier" } } },
        };
        var result = PatreonTierFilter.Filter(new List<EntitledTier> { t1, t2 }, mappings);
        Assert.AreEqual(new[] { "A-tier" }, result.ToArray(), "Lexicographic tiebreaker: A-tier < Z-tier, A wins.");
    }

    [Test]
    public void NullAmountCentsOnOneMapping_PrefersExplicitOverNull()
    {
        var bronze = EntitledTierBuilder.Bronze();
        var nullTier = new EntitledTierBuilder().WithTierId("6482057").WithAmountCents(null).Build();
        var result = PatreonTierFilter.Filter(new List<EntitledTier> { nullTier, bronze }, _mappings);
        Assert.AreEqual(new[] { "6482051" }, result.ToArray(), "Null amounts treated as lowest priority; explicit Bronze wins.");
    }

    [Test]
    public void AllTieredAmountCentsZero_PicksDeterministically()
    {
        var t1 = new EntitledTierBuilder().WithTierId("6482051").WithAmountCents(0).Build();
        var t2 = new EntitledTierBuilder().WithTierId("6482057").WithAmountCents(0).Build();
        var result = PatreonTierFilter.Filter(new List<EntitledTier> { t2, t1 }, _mappings);
        Assert.AreEqual(new[] { "6482051" }, result.ToArray(), "Tiebreaker by tier ID applies even at 0 amount.");
    }

    [Test]
    public void NegativeAmountCents_TreatedAsLowestPriority()
    {
        var negative = new EntitledTierBuilder().WithTierId("6482051").WithAmountCents(-100).Build();
        var positive = new EntitledTierBuilder().WithTierId("6482070").WithAmountCents(100).Build();
        var result = PatreonTierFilter.Filter(new List<EntitledTier> { negative, positive }, _mappings);
        Assert.AreEqual(new[] { "6482070" }, result.ToArray());
    }

    [Test]
    public void NegativeAmount_LosesToNullAmount_TiebreakerByTierIdOrdinal()
    {
        // Both negative and null are mapped to long.MinValue; lexicographic tiebreaker decides.
        var negative = new EntitledTierBuilder().WithTierId("Z-tier").WithAmountCents(-100).Build();
        var nullAmt = new EntitledTierBuilder().WithTierId("A-tier").WithAmountCents(null).Build();
        var mappings = new List<ProductMapping>
        {
            new ProductMapping { Type = ProductMappingType.Tiered, ProductProviders = new() { new ProductProviderPair { ProviderId = "patreon", ProductId = "Z-tier" } } },
            new ProductMapping { Type = ProductMappingType.Tiered, ProductProviders = new() { new ProductProviderPair { ProviderId = "patreon", ProductId = "A-tier" } } },
        };
        var result = PatreonTierFilter.Filter(new List<EntitledTier> { negative, nullAmt }, mappings);
        Assert.AreEqual(new[] { "A-tier" }, result.ToArray(),
            "Negative and null amounts both rank as long.MinValue; lexicographic tiebreaker → A-tier wins.");
    }

    [Test]
    public void VeryLargeAmountCents_DoesNotOverflow()
    {
        var huge = new EntitledTierBuilder().WithTierId("6482051").WithAmountCents(long.MaxValue).Build();
        var small = new EntitledTierBuilder().WithTierId("6482070").WithAmountCents(100).Build();
        var result = PatreonTierFilter.Filter(new List<EntitledTier> { small, huge }, _mappings);
        Assert.AreEqual(new[] { "6482051" }, result.ToArray());
    }

    [Test]
    public void MixedTieredAndOneTime_HighestTieredWins_OneTimesPreserved()
    {
        var oneTime = new EntitledTierBuilder().WithTierId("onetime-1").WithAmountCents(50).Build();
        var input = new List<EntitledTier> { EntitledTierBuilder.Bronze(), oneTime, EntitledTierBuilder.Gold() };
        var result = PatreonTierFilter.Filter(input, _mappings);
        CollectionAssert.AreEquivalent(new[] { "6482070", "onetime-1" }, result.ToArray(),
            "Highest tiered (Gold) plus all non-tiered passthroughs.");
    }
}
