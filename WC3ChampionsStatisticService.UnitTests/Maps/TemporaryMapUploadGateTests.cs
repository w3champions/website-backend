using System;
using System.Collections.Generic;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// The in-flight bounds: one upload per battleTag and
/// <see cref="TemporaryMapLimits.MaxConcurrentUploads"/> per process, in one gate. A slot is held from the
/// controller's acquire until its dispose; both refusals look the same to the client and name their bound to the caller.
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

        Assert.That(gate.TryAcquire(BattleTag, out var slot, out var refusedBy), Is.True);
        Assert.That(slot, Is.Not.Null);
        Assert.That(refusedBy, Is.EqualTo(TemporaryMapUploadGateBound.None));
        Assert.That(gate.InFlight, Is.EqualTo(1));

        slot.Dispose();

        Assert.That(gate.InFlight, Is.Zero);
    }

    [Test]
    public void ASecondUploadOfTheSameBattleTag_IsRefused_WhileTheFirstIsInFlight()
    {
        var gate = new TemporaryMapUploadGate();
        Assert.That(gate.TryAcquire(BattleTag, out var first, out _), Is.True);

        Assert.That(gate.TryAcquire(BattleTag, out var refused, out var refusedBy), Is.False);

        Assert.That(refused, Is.Null);
        Assert.That(refusedBy, Is.EqualTo(TemporaryMapUploadGateBound.PerBattleTag));
        Assert.That(gate.InFlight, Is.EqualTo(1), "a refusal holds nothing");
        first.Dispose();
        Assert.That(gate.TryAcquire(BattleTag, out var second, out _), Is.True, "the battleTag is free again");
        second.Dispose();
    }

    [Test]
    public void DifferentBattleTags_EachGetASlot_UpToTheProcessCap()
    {
        var gate = new TemporaryMapUploadGate();
        var slots = new List<IDisposable>();
        for (var i = 0; i < TemporaryMapLimits.MaxConcurrentUploads; i++)
        {
            Assert.That(gate.TryAcquire($"player{i}#1", out var slot, out _), Is.True, $"upload {i + 1} fits under the cap");
            slots.Add(slot);
        }

        Assert.That(gate.TryAcquire("ninth#1", out var ninth, out var refusedBy), Is.False, "the cap is process-wide, not per battleTag");
        Assert.That(ninth, Is.Null);
        Assert.That(refusedBy, Is.EqualTo(TemporaryMapUploadGateBound.Global));
        Assert.That(gate.InFlight, Is.EqualTo(TemporaryMapLimits.MaxConcurrentUploads));

        slots[0].Dispose();

        Assert.That(gate.TryAcquire("ninth#1", out ninth, out _), Is.True, "a released slot is reusable at once");
        ninth.Dispose();
        slots.ForEach(s => s.Dispose());
        Assert.That(gate.InFlight, Is.Zero);
    }

    [Test]
    public void ThePerBattleTagBound_IsCheckedBeforeTheProcessCap()
    {
        // A player whose upload is in flight is refused for that reason even when the cap is also reached: the bound
        // named is the one the player can act on (wait for their own upload, not for everyone's).
        var gate = new TemporaryMapUploadGate();
        var slots = new List<IDisposable>();
        Assert.That(gate.TryAcquire(BattleTag, out var own, out _), Is.True);
        slots.Add(own);
        for (var i = 1; i < TemporaryMapLimits.MaxConcurrentUploads; i++)
        {
            Assert.That(gate.TryAcquire($"player{i}#1", out var slot, out _), Is.True);
            slots.Add(slot);
        }

        Assert.That(gate.TryAcquire(BattleTag, out _, out var refusedBy), Is.False);

        Assert.That(refusedBy, Is.EqualTo(TemporaryMapUploadGateBound.PerBattleTag));
        slots.ForEach(s => s.Dispose());
    }

    [Test]
    public void AGlobalRefusal_HoldsNothingForTheRefusedBattleTag()
    {
        var gate = new TemporaryMapUploadGate();
        var others = new List<IDisposable>();
        for (var i = 0; i < TemporaryMapLimits.MaxConcurrentUploads; i++)
        {
            gate.TryAcquire($"player{i}#1", out var slot, out _);
            others.Add(slot);
        }

        Assert.That(gate.TryAcquire(BattleTag, out _, out var refusedBy), Is.False, "refused by the process cap");
        Assert.That(refusedBy, Is.EqualTo(TemporaryMapUploadGateBound.Global));
        Assert.That(gate.InFlight, Is.EqualTo(TemporaryMapLimits.MaxConcurrentUploads), "the refusal took nothing");
        others[0].Dispose();

        Assert.That(gate.TryAcquire(BattleTag, out var slot2, out _), Is.True,
            "nothing of the refused battleTag stayed held: it is admitted the moment a slot frees");
        slot2.Dispose();
        others.ForEach(s => s.Dispose());
    }

    [Test]
    public void DisposingASlotTwice_ReleasesItOnce()
    {
        var gate = new TemporaryMapUploadGate();
        gate.TryAcquire(BattleTag, out var slot, out _);
        gate.TryAcquire("other#1", out var other, out _);

        slot.Dispose();
        slot.Dispose();

        Assert.That(gate.InFlight, Is.EqualTo(1), "the other upload's slot is untouched");
        Assert.That(gate.TryAcquire("other#1", out _, out _), Is.False, "the other battleTag is still in flight");
        other.Dispose();
        Assert.That(gate.InFlight, Is.Zero);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ABlankBattleTag_IsACallerBug(string battleTag)
    {
        var gate = new TemporaryMapUploadGate();

        Assert.Catch<ArgumentException>(() => gate.TryAcquire(battleTag, out _, out _));

        Assert.That(gate.InFlight, Is.Zero);
    }
}
