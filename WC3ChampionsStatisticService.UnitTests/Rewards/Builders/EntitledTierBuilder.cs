using W3C.Domain.Rewards.Entities;

namespace WC3ChampionsStatisticService.UnitTests.Rewards.Builders;

public class EntitledTierBuilder
{
    private string _tierId = "default-tier-id";
    private long? _amountCents = 100;
    private string _title = "Default Tier";

    public EntitledTierBuilder WithTierId(string id) { _tierId = id; return this; }
    public EntitledTierBuilder WithAmountCents(long? cents) { _amountCents = cents; return this; }
    public EntitledTierBuilder WithTitle(string title) { _title = title; return this; }

    public EntitledTier Build() => new EntitledTier
    {
        TierId = _tierId,
        AmountCents = _amountCents,
        Title = _title
    };

    public static EntitledTier Bronze() => new EntitledTierBuilder().WithTierId("6482051").WithAmountCents(100).WithTitle("Bronze Tier Supporter").Build();
    public static EntitledTier Silver() => new EntitledTierBuilder().WithTierId("6482057").WithAmountCents(500).WithTitle("Silver Tier Supporter").Build();
    public static EntitledTier Gold() => new EntitledTierBuilder().WithTierId("6482070").WithAmountCents(1000).WithTitle("Gold Tier Supporter").Build();
}
