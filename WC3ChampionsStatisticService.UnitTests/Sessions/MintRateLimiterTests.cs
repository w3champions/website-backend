using System;
using System.Threading;
using System.Threading.Tasks;
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

    [Test]
    public void DefaultOverload_HoldsForTheWholeTicketMintWindow()
    {
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;

        for (var i = 0; i < Limit; i++)
        {
            limiter.TryAcquire("bt:peter#123", Limit, now);
        }

        var lastTickOfTheWindow = now + SessionLimits.TicketMintWindow - TimeSpan.FromTicks(1);
        Assert.IsFalse(limiter.TryAcquire("bt:peter#123", Limit, lastTickOfTheWindow), "still inside the one-minute window");
    }

    [Test]
    public void ExplicitWindow_IsHonouredIndependentlyOfTheDefault()
    {
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;
        var hour = TimeSpan.FromHours(1);

        for (var i = 0; i < Limit; i++)
        {
            Assert.IsTrue(limiter.TryAcquire("tm-upload:peter#123", Limit, now, hour, out _));
        }

        Assert.IsFalse(limiter.TryAcquire("tm-upload:peter#123", Limit, now, hour, out _));

        var afterTheDefaultWindow = now + SessionLimits.TicketMintWindow + TimeSpan.FromSeconds(1);
        Assert.IsFalse(limiter.TryAcquire("tm-upload:peter#123", Limit, afterTheDefaultWindow, hour, out _),
            "an hour-long window must not roll over after one minute");

        var afterTheHour = now + hour + TimeSpan.FromSeconds(1);
        Assert.IsTrue(limiter.TryAcquire("tm-upload:peter#123", Limit, afterTheHour, hour, out _));
    }

    [Test]
    public void DeniedAcquire_ReportsTheRemainingWindowAsRetryAfter()
    {
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;
        var hour = TimeSpan.FromHours(1);

        for (var i = 0; i < Limit; i++)
        {
            limiter.TryAcquire("tm-upload:peter#123", Limit, now, hour, out _);
        }

        var tenMinutesIn = now + TimeSpan.FromMinutes(10);
        Assert.IsFalse(limiter.TryAcquire("tm-upload:peter#123", Limit, tenMinutesIn, hour, out var retryAfter));

        Assert.AreEqual(TimeSpan.FromMinutes(50), retryAfter);
    }

    [Test]
    public void AllowedAcquire_ReportsZeroRetryAfter()
    {
        var limiter = new MintRateLimiter();

        Assert.IsTrue(limiter.TryAcquire("tm-upload:peter#123", Limit, DateTime.UtcNow, TimeSpan.FromHours(1), out var retryAfter));

        Assert.AreEqual(TimeSpan.Zero, retryAfter);
    }

    [Test]
    public void ExplicitWindow_ClosesExactlyAtItsEdge()
    {
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;
        var hour = TimeSpan.FromHours(1);
        limiter.TryAcquire("tm-upload:peter#123", 1, now, hour, out _);

        var lastTick = now + hour - TimeSpan.FromTicks(1);
        Assert.IsFalse(limiter.TryAcquire("tm-upload:peter#123", 1, lastTick, hour, out var retryAfter), "the last tick is inside the window");
        Assert.AreEqual(TimeSpan.FromTicks(1), retryAfter, "retryAfter is the exact remainder, never zero or negative while denied");

        Assert.IsTrue(limiter.TryAcquire("tm-upload:peter#123", 1, now + hour, hour, out retryAfter), "the window is closed at start + window");
        Assert.AreEqual(TimeSpan.Zero, retryAfter);
    }

    [Test]
    public void StaleWindows_ArePurgedExactlyAtTheirEdge()
    {
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;
        var hour = TimeSpan.FromHours(1);
        limiter.TryAcquire("tm-upload:peter#123", Limit, now, hour, out _);

        limiter.TryAcquire("tm-upload:hans#456", Limit, now + hour, hour, out _);

        Assert.AreEqual(1, limiter.Count, "a window is stale at start + window, the same instant it stops counting");
    }

    [Test]
    public void ALiveWindow_KeepsTheLengthItOpenedWith_WhenCalledWithAShorterOne()
    {
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;
        limiter.TryAcquire("tm-upload:peter#123", 1, now, TimeSpan.FromHours(1), out _);

        var twoMinutesIn = now + TimeSpan.FromMinutes(2);
        Assert.IsFalse(limiter.TryAcquire("tm-upload:peter#123", 1, twoMinutesIn, TimeSpan.FromMinutes(1), out var retryAfter),
            "a shorter window passed later must not shorten the running hour");
        Assert.AreEqual(TimeSpan.FromMinutes(58), retryAfter, "retryAfter comes from the stored window, not the argument");

        var afterTheHour = now + TimeSpan.FromHours(1);
        Assert.IsTrue(limiter.TryAcquire("tm-upload:peter#123", 1, afterTheHour, TimeSpan.FromMinutes(1), out _));
        Assert.IsFalse(limiter.TryAcquire("tm-upload:peter#123", 1, afterTheHour + TimeSpan.FromSeconds(30), TimeSpan.FromHours(1), out retryAfter),
            "the next window opened with the one-minute length passed when it opened");
        Assert.AreEqual(TimeSpan.FromSeconds(30), retryAfter);
    }

    [Test]
    public void ALiveWindow_KeepsTheLengthItOpenedWith_WhenCalledWithALongerOne()
    {
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;
        limiter.TryAcquire("tm-precheck:peter#123", 1, now, TimeSpan.FromMinutes(1), out _);

        var thirtySecondsIn = now + TimeSpan.FromSeconds(30);
        Assert.IsFalse(limiter.TryAcquire("tm-precheck:peter#123", 1, thirtySecondsIn, TimeSpan.FromHours(1), out var retryAfter));
        Assert.AreEqual(TimeSpan.FromSeconds(30), retryAfter, "a longer window passed later does not extend the running minute");

        Assert.IsTrue(limiter.TryAcquire("tm-precheck:peter#123", 1, now + TimeSpan.FromMinutes(1), TimeSpan.FromHours(1), out _));
    }

    [Test]
    public void AnAllowedCallWithADifferentWindow_DoesNotChangeTheStoredLength()
    {
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;
        limiter.TryAcquire("tm-upload:peter#123", 2, now, TimeSpan.FromHours(1), out _);

        Assert.IsTrue(limiter.TryAcquire("tm-upload:peter#123", 2, now + TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), out _),
            "the second call fits the limit and increments the live hour window");

        var twoMinutesIn = now + TimeSpan.FromMinutes(2);
        Assert.IsFalse(limiter.TryAcquire("tm-upload:peter#123", 2, twoMinutesIn, TimeSpan.FromHours(1), out var retryAfter),
            "the increment kept the hour, so the window has not rolled over after two minutes");
        Assert.AreEqual(TimeSpan.FromMinutes(58), retryAfter);
    }

    [Test]
    public void WindowsOfDifferentLengths_CoexistInOneLimiter()
    {
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;

        limiter.TryAcquire("tm-upload:peter#123", 1, now, TimeSpan.FromHours(1), out _);
        limiter.TryAcquire("tm-precheck:peter#123", 1, now, TimeSpan.FromMinutes(1), out _);

        var twoMinutesIn = now + TimeSpan.FromMinutes(2);
        Assert.IsFalse(limiter.TryAcquire("tm-upload:peter#123", 1, twoMinutesIn, TimeSpan.FromHours(1), out _),
            "the hour bucket is still exhausted");
        Assert.IsTrue(limiter.TryAcquire("tm-precheck:peter#123", 1, twoMinutesIn, TimeSpan.FromMinutes(1), out _),
            "the minute bucket rolled over");
    }

    [Test]
    public void StaleWindows_ArePurgedAgainstTheirOwnWindowLength()
    {
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;
        limiter.TryAcquire("tm-upload:peter#123", Limit, now, TimeSpan.FromHours(1), out _);
        Assert.AreEqual(1, limiter.Count);

        // Two minutes later the one-minute default would have purged it; the stored hour must not.
        limiter.TryAcquire("bt:hans#456", Limit, now + TimeSpan.FromMinutes(2));
        Assert.AreEqual(2, limiter.Count, "an hour-long window is not stale after two minutes");

        limiter.TryAcquire("bt:greta#789", Limit, now + TimeSpan.FromHours(2));
        Assert.AreEqual(1, limiter.Count, "both older windows are now past their own lengths");
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void NonPositiveWindow_IsRejected(long windowTicks)
    {
        var limiter = new MintRateLimiter();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => limiter.TryAcquire("tm-upload:peter#123", Limit, DateTime.UtcNow, TimeSpan.FromTicks(windowTicks), out _),
            "a window that is already over would silently disable the limit");
        Assert.AreEqual(0, limiter.Count);
    }

    [Test]
    public void ConcurrentAcquires_NeverExceedTheLimitPerKey()
    {
        const int keys = 4;
        const int limit = 1_000;
        var limiter = new MintRateLimiter();
        var now = DateTime.UtcNow;
        var allowed = new int[keys];

        Parallel.For(0, keys * limit * 5, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var key = i % keys;
            if (limiter.TryAcquire($"tm-upload:player#{key}", limit, now, TimeSpan.FromHours(1), out _))
            {
                Interlocked.Increment(ref allowed[key]);
            }
        });

        Assert.That(allowed, Is.All.EqualTo(limit));
    }
}
