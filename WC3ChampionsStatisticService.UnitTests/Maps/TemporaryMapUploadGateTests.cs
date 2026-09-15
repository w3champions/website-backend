using System;
using System.Collections.Generic;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// The in-flight bounds of D7 and the Task 2 H1 ruling: one upload per battleTag and
/// <see cref="TemporaryMapLimits.MaxConcurrentUploads"/> per process, in one gate. A slot is held from the
/// controller's acquire until its dispose; both refusals look the same to the caller.
/// </summary>
[TestFixture]
public class TemporaryMapUploadGateTests
{
    private const string BattleTag = "peter#123";

    [Test]
    public void TheLimits_ArePinned()
    {
        Assert.That(TemporaryMapLimits.MaxConcurrentUploads, Is.EqualTo(8));
        Assert.That(TemporaryMapLimits.ConcurrentUploadRetryAfterSeconds, Is.EqualTo(30));
    }

    [Test]
    public void TheFirstUploadOfABattleTag_GetsASlot_UntilItIsDisposed()
    {
        var gate = new TemporaryMapUploadGate();

        Assert.That(gate.TryAcquire(BattleTag, out var slot), Is.True);
        Assert.That(slot, Is.Not.Null);
        Assert.That(gate.InFlight, Is.EqualTo(1));

        slot.Dispose();

        Assert.That(gate.InFlight, Is.Zero);
    }

    [Test]
    public void ASecondUploadOfTheSameBattleTag_IsRefused_WhileTheFirstIsInFlight()
    {
        var gate = new TemporaryMapUploadGate();
        Assert.That(gate.TryAcquire(BattleTag, out var first), Is.True);

        Assert.That(gate.TryAcquire(BattleTag, out var refused), Is.False);

        Assert.That(refused, Is.Null);
        Assert.That(gate.InFlight, Is.EqualTo(1), "a refusal holds nothing");
        first.Dispose();
        Assert.That(gate.TryAcquire(BattleTag, out var second), Is.True, "the battleTag is free again");
        second.Dispose();
    }

    [Test]
    public void DifferentBattleTags_EachGetASlot_UpToTheProcessCap()
    {
        var gate = new TemporaryMapUploadGate();
        var slots = new List<IDisposable>();
        for (var i = 0; i < TemporaryMapLimits.MaxConcurrentUploads; i++)
        {
            Assert.That(gate.TryAcquire($"player{i}#1", out var slot), Is.True, $"upload {i + 1} fits under the cap");
            slots.Add(slot);
        }

        Assert.That(gate.TryAcquire("ninth#1", out var ninth), Is.False, "the cap is process-wide, not per battleTag");
        Assert.That(ninth, Is.Null);
        Assert.That(gate.InFlight, Is.EqualTo(TemporaryMapLimits.MaxConcurrentUploads));

        slots[0].Dispose();

        Assert.That(gate.TryAcquire("ninth#1", out ninth), Is.True, "a released slot is reusable at once");
        ninth.Dispose();
        slots.ForEach(s => s.Dispose());
        Assert.That(gate.InFlight, Is.Zero);
    }

    [Test]
    public void AGlobalRefusal_ReleasesThePerBattleTagSlotItTookFirst()
    {
        var gate = new TemporaryMapUploadGate();
        var others = new List<IDisposable>();
        for (var i = 0; i < TemporaryMapLimits.MaxConcurrentUploads; i++)
        {
            gate.TryAcquire($"player{i}#1", out var slot);
            others.Add(slot);
        }

        Assert.That(gate.TryAcquire(BattleTag, out _), Is.False, "refused by the process cap");
        others[0].Dispose();

        Assert.That(gate.TryAcquire(BattleTag, out var slot2), Is.True,
            "the per-battleTag slot taken before the global refusal must not stay held");
        slot2.Dispose();
        others.ForEach(s => s.Dispose());
    }

    [Test]
    public void DisposingASlotTwice_ReleasesItOnce()
    {
        var gate = new TemporaryMapUploadGate();
        gate.TryAcquire(BattleTag, out var slot);
        gate.TryAcquire("other#1", out var other);

        slot.Dispose();
        slot.Dispose();

        Assert.That(gate.InFlight, Is.EqualTo(1), "the other upload's slot is untouched");
        Assert.That(gate.TryAcquire("other#1", out _), Is.False, "the other battleTag is still in flight");
        other.Dispose();
        Assert.That(gate.InFlight, Is.Zero);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ABlankBattleTag_IsACallerBug(string battleTag)
    {
        var gate = new TemporaryMapUploadGate();

        Assert.Catch<ArgumentException>(() => gate.TryAcquire(battleTag, out _));

        Assert.That(gate.InFlight, Is.Zero);
    }
}
