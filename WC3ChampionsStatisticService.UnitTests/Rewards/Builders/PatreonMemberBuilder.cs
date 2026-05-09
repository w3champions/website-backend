using System.Collections.Generic;
using W3C.Domain.Rewards.Entities;
using W3ChampionsStatisticService.Rewards.Providers.Patreon;

namespace WC3ChampionsStatisticService.UnitTests.Rewards.Builders;

public class PatreonMemberBuilder
{
    private string _patreonUserId = "default-patreon-user-id";
    private string _email = "test@example.com";
    private string _patronStatus = "active_patron";
    private string _lastChargeStatus = "Paid";
    private List<EntitledTier> _entitledTiers = new();

    public PatreonMemberBuilder WithPatreonUserId(string id) { _patreonUserId = id; return this; }
    public PatreonMemberBuilder WithEmail(string e) { _email = e; return this; }
    public PatreonMemberBuilder WithPatronStatus(string s) { _patronStatus = s; return this; }
    public PatreonMemberBuilder WithLastChargeStatus(string s) { _lastChargeStatus = s; return this; }
    public PatreonMemberBuilder WithTiers(params EntitledTier[] tiers) { _entitledTiers = new List<EntitledTier>(tiers); return this; }

    public PatreonMember Build() => new PatreonMember
    {
        PatreonUserId = _patreonUserId,
        Email = _email,
        PatronStatus = _patronStatus,
        LastChargeStatus = _lastChargeStatus,
        EntitledTiers = _entitledTiers
    };
}
