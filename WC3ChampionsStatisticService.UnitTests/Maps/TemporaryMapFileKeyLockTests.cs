using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// The per-fileKey async mutex: one holder per key, independent keys, a cancellable wait that
/// leaves nothing behind, and entries that disappear with their last holder or waiter.
/// </summary>
[TestFixture]
public class TemporaryMapFileKeyLockTests
{
    private const string Key = "W3Champions/CustomGames/Legion TD-a9993e36.w3x";

    /// <summary>Only fails a broken build that would otherwise hang; no assertion depends on timing.</summary>
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    [Test]
    public async Task ASecondAcquireOfTheSameKey_WaitsUntilTheFirstIsReleased()
    {
        var keyLock = new TemporaryMapFileKeyLock();
        var first = await keyLock.AcquireAsync(Key, CancellationToken.None);

        var second = keyLock.AcquireAsync(Key, CancellationToken.None);

        Assert.That(second.IsCompleted, Is.False, "the key is held");
        first.Dispose();
        (await second.WaitAsync(HangGuard)).Dispose();
        Assert.That(keyLock.Count, Is.Zero, "the entry leaves with its last holder");
    }

    [Test]
    public async Task DifferentKeys_DoNotWaitForEachOther()
    {
        var keyLock = new TemporaryMapFileKeyLock();
        using var first = await keyLock.AcquireAsync(Key, CancellationToken.None);

        var other = keyLock.AcquireAsync("W3Champions/CustomGames/Other-0123abcd.w3x", CancellationToken.None);

        Assert.That(other.IsCompletedSuccessfully, Is.True);
        Assert.That(keyLock.Count, Is.EqualTo(2));
        other.Result.Dispose();
    }

    [Test]
    public async Task CancellingAWait_Throws_AndForgetsTheWaiter()
    {
        var keyLock = new TemporaryMapFileKeyLock();
        using var aborted = new CancellationTokenSource();
        var held = await keyLock.AcquireAsync(Key, CancellationToken.None);
        var waiting = keyLock.AcquireAsync(Key, aborted.Token);

        aborted.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => waiting.WaitAsync(HangGuard));
        Assert.That(keyLock.Count, Is.EqualTo(1), "only the holder keeps the entry");
        held.Dispose();
        Assert.That(keyLock.Count, Is.Zero);
        var again = keyLock.AcquireAsync(Key, CancellationToken.None);
        Assert.That(again.IsCompletedSuccessfully, Is.True, "the cancelled waiter never took the key");
        again.Result.Dispose();
    }

    [Test]
    public async Task OnlyAnAcquireThatFindsTheKeyInUse_ReportsContention()
    {
        var contended = new List<string>();
        var keyLock = new TemporaryMapFileKeyLock { OnContended = contended.Add };

        var first = await keyLock.AcquireAsync(Key, CancellationToken.None);
        Assert.That(contended, Is.Empty, "a free key is not contended");
        var second = keyLock.AcquireAsync(Key, CancellationToken.None);

        Assert.That(contended, Is.EqualTo(new[] { Key }));
        first.Dispose();
        (await second.WaitAsync(HangGuard)).Dispose();
    }
}
