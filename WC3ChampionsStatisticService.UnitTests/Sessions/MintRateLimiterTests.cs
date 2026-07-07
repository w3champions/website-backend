using System;
using NUnit.Framework;
using W3ChampionsStatisticService.Sessions;

namespace WC3ChampionsStatisticService.Tests.Sessions;

[TestFixture]
public class MintRateLimiterTests
{
    private const int Limit = 3;

    [Test]
    public void AllowsUpToLimit_WithinWindow()
    {
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;

        for (var i = 0; i < Limit; i++)
        {
            Assert.IsTrue(limiter.TryAcquire("bt:peter#123", Limit, now), $"call {i + 1} of {Limit} must be allowed");
        }
    }

    [Test]
    public void BlocksLimitPlusOne_WithinWindow()
    {
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;

        for (var i = 0; i < Limit; i++)
        {
            limiter.TryAcquire("bt:peter#123", Limit, now);
        }

        Assert.IsFalse(limiter.TryAcquire("bt:peter#123", Limit, now), "the (limit+1)th call in the window must be blocked");
    }

    [Test]
    public void ResetsAfterWindow()
    {
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;

        for (var i = 0; i < Limit; i++)
        {
            limiter.TryAcquire("bt:peter#123", Limit, now);
        }
        Assert.IsFalse(limiter.TryAcquire("bt:peter#123", Limit, now), "exhausted within the window");

        var afterWindow = now + SessionLimits.TicketMintWindow + TimeSpan.FromSeconds(1);
        Assert.IsTrue(limiter.TryAcquire("bt:peter#123", Limit, afterWindow), "the window rolled over → allowed again");
    }

    [Test]
    public void KeysAreIndependent()
    {
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;

        for (var i = 0; i < Limit; i++)
        {
            limiter.TryAcquire("bt:peter#123", Limit, now);
        }
        Assert.IsFalse(limiter.TryAcquire("bt:peter#123", Limit, now), "peter is exhausted");
        Assert.IsTrue(limiter.TryAcquire("bt:hans#456", Limit, now), "a different battleTag has its own window");
    }

    [Test]
    public void StaleWindows_ArePurged()
    {
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;
        limiter.TryAcquire("bt:peter#123", Limit, now);
        Assert.AreEqual(1, limiter.Count);

        // A later call for another key past the window purges the stale one opportunistically.
        var afterWindow = now + SessionLimits.TicketMintWindow + TimeSpan.FromSeconds(1);
        limiter.TryAcquire("bt:hans#456", Limit, afterWindow);
        Assert.AreEqual(1, limiter.Count, "the stale peter window is purged; only hans remains");
    }
}
