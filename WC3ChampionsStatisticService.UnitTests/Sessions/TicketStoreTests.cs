using System;
using NUnit.Framework;
using W3ChampionsStatisticService.Sessions;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Sessions;

[TestFixture]
public class TicketStoreTests
{
    private static W3CUserAuthenticationDto Identity(string battleTag) =>
        new W3CUserAuthenticationDto { BattleTag = battleTag, Name = battleTag.Split('#')[0] };

    [Test]
    public void MintThenConsume_ReturnsBoundIdentity()
    {
        var store = new TicketStore();
        var now = DateTime.UtcNow;

        var ticket = store.Mint(Identity("peter#123"), now);

        Assert.AreEqual(64, ticket.Length, "Ticket must be 64 hex chars (32 random bytes).");
        var consumed = store.TryConsume(ticket, now, out var identity);
        Assert.IsTrue(consumed);
        Assert.IsNotNull(identity);
        Assert.AreEqual("peter#123", identity.BattleTag);
    }

    [Test]
    public void SecondConsume_OfSameTicket_Fails_SingleUse()
    {
        var store = new TicketStore();
        var now = DateTime.UtcNow;
        var ticket = store.Mint(Identity("peter#123"), now);

        Assert.IsTrue(store.TryConsume(ticket, now, out _), "First consume must succeed.");

        var second = store.TryConsume(ticket, now, out var identity);
        Assert.IsFalse(second, "A ticket is single-use; the second consume must fail.");
        Assert.IsNull(identity);
        Assert.AreEqual(0, store.Count);
    }

    [Test]
    public void ExpiredTicket_Fails_AndIsBurned()
    {
        var store = new TicketStore();
        var issuedAt = DateTime.UtcNow;
        var ticket = store.Mint(Identity("peter#123"), issuedAt);

        // now > issued + 60s → expired.
        var afterTtl = issuedAt + SessionLimits.TicketTtl + TimeSpan.FromSeconds(1);
        var consumed = store.TryConsume(ticket, afterTtl, out var identity);

        Assert.IsFalse(consumed, "A ticket presented after its TTL must fail.");
        Assert.IsNull(identity);
        Assert.AreEqual(0, store.Count, "An expired ticket must still be burned on consume.");
    }

    [Test]
    public void TicketAtExactTtlBoundary_StillValid()
    {
        var store = new TicketStore();
        var issuedAt = DateTime.UtcNow;
        var ticket = store.Mint(Identity("peter#123"), issuedAt);

        // now == issued + 60s exactly is NOT past the TTL (check is `now > issued + TTL`).
        var consumed = store.TryConsume(ticket, issuedAt + SessionLimits.TicketTtl, out var identity);

        Assert.IsTrue(consumed);
        Assert.AreEqual("peter#123", identity.BattleTag);
    }

    [Test]
    public void UnknownTicket_Fails()
    {
        var store = new TicketStore();

        var consumed = store.TryConsume("DEADBEEF", DateTime.UtcNow, out var identity);

        Assert.IsFalse(consumed);
        Assert.IsNull(identity);
    }

    [Test]
    public void Mint_PurgesExpiredTickets()
    {
        var store = new TicketStore();
        var t0 = DateTime.UtcNow;
        store.Mint(Identity("stale#1"), t0);
        Assert.AreEqual(1, store.Count);

        // Minting well after the first ticket's TTL purges it on the way in.
        store.Mint(Identity("fresh#2"), t0 + SessionLimits.TicketTtl + TimeSpan.FromSeconds(1));
        Assert.AreEqual(1, store.Count, "Expired tickets are purged on mint; only the fresh one remains.");
    }
}
