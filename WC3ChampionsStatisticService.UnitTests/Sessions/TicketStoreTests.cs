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
    public void MintThenRedeem_ReturnsBoundIdentity()
    {
        var store = new TicketStore();
        var now = DateTime.UtcNow;

        var ticket = store.Mint(Identity("peter#123"), now);

        Assert.AreEqual(64, ticket.Length, "Ticket must be 64 hex chars (32 random bytes).");
        var redeemed = store.TryRedeem(ticket, "conn-1", now, out var identity);
        Assert.IsTrue(redeemed);
        Assert.IsNotNull(identity);
        Assert.AreEqual("peter#123", identity.BattleTag);
    }

    [Test]
    public void Redeem_AfterRelease_SucceedsAgain_WithSameIdentity()
    {
        var store = new TicketStore();
        var now = DateTime.UtcNow;
        var ticket = store.Mint(Identity("peter#123"), now);

        Assert.IsTrue(store.TryRedeem(ticket, "conn-1", now, out _));
        store.Release("conn-1");

        Assert.IsTrue(store.TryRedeem(ticket, "conn-2", now + TimeSpan.FromSeconds(5), out var identity),
            "A reconnect inside the TTL must be able to reuse the ticket once the first connection closed.");
        Assert.AreEqual("peter#123", identity.BattleTag);
    }

    [Test]
    public void Redeem_WhileHeld_Fails_AndRevokesTicket()
    {
        var store = new TicketStore();
        var now = DateTime.UtcNow;
        var ticket = store.Mint(Identity("peter#123"), now);
        Assert.IsTrue(store.TryRedeem(ticket, "conn-1", now, out _));

        var concurrent = store.TryRedeem(ticket, "conn-2", now, out var identity);

        Assert.IsFalse(concurrent, "A second live connection on one ticket is a replay.");
        Assert.IsNull(identity);
        Assert.AreEqual(0, store.Count, "A replayed ticket must be revoked.");

        store.Release("conn-1");
        Assert.IsFalse(store.TryRedeem(ticket, "conn-3", now, out _), "A revoked ticket stays revoked after the holder leaves.");
    }

    [Test]
    public void Release_ByNonHolder_DoesNotFreeTicket()
    {
        var store = new TicketStore();
        var now = DateTime.UtcNow;
        var ticket = store.Mint(Identity("peter#123"), now);
        Assert.IsTrue(store.TryRedeem(ticket, "conn-1", now, out _));

        store.Release("someone-else");

        Assert.IsFalse(store.TryRedeem(ticket, "conn-2", now, out _), "Only the holding connection can release a ticket.");
    }

    [Test]
    public void Redeem_IsCappedAtMaxRedemptions()
    {
        var store = new TicketStore();
        var now = DateTime.UtcNow;
        var ticket = store.Mint(Identity("peter#123"), now);

        for (var i = 0; i < SessionLimits.TicketMaxRedemptions; i++)
        {
            Assert.IsTrue(store.TryRedeem(ticket, $"conn-{i}", now, out _), $"redemption {i + 1} must succeed");
            store.Release($"conn-{i}");
        }

        Assert.IsFalse(store.TryRedeem(ticket, "conn-over", now, out var identity), "The cap bounds the replay window.");
        Assert.IsNull(identity);
        Assert.AreEqual(0, store.Count, "An exhausted ticket is burned.");
    }

    [Test]
    public void ExpiredTicket_Fails_AndIsBurned()
    {
        var store = new TicketStore();
        var issuedAt = DateTime.UtcNow;
        var ticket = store.Mint(Identity("peter#123"), issuedAt);

        // now > issued + 60s → expired.
        var afterTtl = issuedAt + SessionLimits.TicketTtl + TimeSpan.FromSeconds(1);
        var redeemed = store.TryRedeem(ticket, "conn-1", afterTtl, out var identity);

        Assert.IsFalse(redeemed, "A ticket presented after its TTL must fail.");
        Assert.IsNull(identity);
        Assert.AreEqual(0, store.Count, "An expired ticket must still be burned on presentation.");
    }

    [Test]
    public void Reuse_DoesNotExtendTtl()
    {
        var store = new TicketStore();
        var issuedAt = DateTime.UtcNow;
        var ticket = store.Mint(Identity("peter#123"), issuedAt);
        Assert.IsTrue(store.TryRedeem(ticket, "conn-1", issuedAt + TimeSpan.FromSeconds(50), out _));
        store.Release("conn-1");

        Assert.IsFalse(store.TryRedeem(ticket, "conn-2", issuedAt + SessionLimits.TicketTtl + TimeSpan.FromSeconds(1), out _),
            "The TTL runs from mint; a redemption must not extend it.");
    }

    [Test]
    public void TicketAtExactTtlBoundary_StillValid()
    {
        var store = new TicketStore();
        var issuedAt = DateTime.UtcNow;
        var ticket = store.Mint(Identity("peter#123"), issuedAt);

        // now == issued + 60s exactly is NOT past the TTL (check is `now > issued + TTL`).
        var redeemed = store.TryRedeem(ticket, "conn-1", issuedAt + SessionLimits.TicketTtl, out var identity);

        Assert.IsTrue(redeemed);
        Assert.AreEqual("peter#123", identity.BattleTag);
    }

    [Test]
    public void UnknownTicket_Fails()
    {
        var store = new TicketStore();

        var redeemed = store.TryRedeem("DEADBEEF", "conn-1", DateTime.UtcNow, out var identity);

        Assert.IsFalse(redeemed);
        Assert.IsNull(identity);
    }

    [Test]
    public void EmptyTicketOrConnection_Fails()
    {
        var store = new TicketStore();
        var now = DateTime.UtcNow;
        var ticket = store.Mint(Identity("peter#123"), now);

        Assert.IsFalse(store.TryRedeem("", "conn-1", now, out _));
        Assert.IsFalse(store.TryRedeem(ticket, "", now, out _), "A redemption must name its connection.");
        Assert.IsTrue(store.TryRedeem(ticket, "conn-1", now, out _), "An empty connection id must not take the ticket.");
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
