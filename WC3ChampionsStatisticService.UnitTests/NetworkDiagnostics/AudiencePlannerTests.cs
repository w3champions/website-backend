using System;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class AudiencePlannerTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Online = ["A#1", "B#2", "C#3", "D#4"];

    private static NetworkDiagnosticRun FleetRun(RunAudience audience, int delivered = 0, DateTime? cancelledAt = null) => new()
    {
        Id = "r",
        Audience = audience,
        WindowStart = Now.AddMinutes(-5),
        WindowEnd = Now.AddMinutes(25),
        CancelledAt = cancelledAt,
        Counters = new RunCounters { Delivered = delivered },
    };

    [Test]
    public void PickInitial_All_TakesEveryone() =>
        Assert.AreEqual(Online, AudiencePlanner.PickInitial(new RunAudience { Kind = AudienceKind.All }, Online, new QueueRandom(0.5)));

    [Test]
    public void PickInitial_Percent_IsBernoulliPerCandidate() =>
        // 10 %: draws below 0.10 are picked
        Assert.AreEqual(new[] { "A#1", "D#4" },
            AudiencePlanner.PickInitial(new RunAudience { Kind = AudienceKind.SamplePercent, Percent = 10 }, Online, new QueueRandom(0.05, 0.5, 0.1, 0.09)));

    [Test]
    public void PickInitial_Count_PicksExactlyCountDistinct()
    {
        var picked = AudiencePlanner.PickInitial(new RunAudience { Kind = AudienceKind.SampleCount, Count = 2 }, Online, new QueueRandom(0.99, 0.0));
        // partial Fisher-Yates: i=0 swaps with index 0+floor(0.99*4)=3 -> D; i=1 swaps with 1+floor(0*3)=1 -> B
        Assert.AreEqual(new[] { "D#4", "B#2" }, picked);
    }

    [Test]
    public void PickInitial_CountLargerThanOnline_TakesAll() =>
        Assert.AreEqual(4, AudiencePlanner.PickInitial(new RunAudience { Kind = AudienceKind.SampleCount, Count = 50 }, Online, new QueueRandom(0.0)).Count);

    [Test]
    public void PickRandom_DoesNotMutateInput()
    {
        var input = new[] { "A#1", "B#2", "C#3" };
        AudiencePlanner.PickRandom(input, 3, new QueueRandom(0.99));
        Assert.AreEqual(new[] { "A#1", "B#2", "C#3" }, input);
    }

    [TestCase(0.0, 0)]
    [TestCase(0.5, 15)]
    [TestCase(0.999999, 29)]
    public void RandomSlot_IsUniformInWindow(double draw, int expectedMinute)
    {
        var slot = AudiencePlanner.RandomSlot(Now, Now.AddMinutes(30), new QueueRandom(draw));
        Assert.AreEqual(expectedMinute, (int)(slot - Now).TotalMinutes);
    }

    [Test]
    public void RandomSlot_ClosedWindow_IsNow() => Assert.AreEqual(Now, AudiencePlanner.RandomSlot(Now, Now.AddMinutes(-1), new QueueRandom(0.5)));

    [Test]
    public void LateJoiner_All_AlwaysInsideWindow() =>
        Assert.IsTrue(AudiencePlanner.ShouldAssignLateJoiner(FleetRun(new RunAudience { Kind = AudienceKind.All }), Now, new QueueRandom(0.99)));

    [TestCase(0.29, true)]
    [TestCase(0.30, false)]
    public void LateJoiner_Percent_UsesProbability(double draw, bool expected) =>
        Assert.AreEqual(expected, AudiencePlanner.ShouldAssignLateJoiner(FleetRun(new RunAudience { Kind = AudienceKind.SamplePercent, Percent = 30 }), Now, new QueueRandom(draw)));

    [TestCase(4, true)]
    [TestCase(5, false)]
    public void LateJoiner_Count_OnlyWhileDeliveredBelowCount(int delivered, bool expected) =>
        Assert.AreEqual(expected, AudiencePlanner.ShouldAssignLateJoiner(FleetRun(new RunAudience { Kind = AudienceKind.SampleCount, Count = 5 }, delivered), Now, new QueueRandom(0.0)));

    [Test]
    public void LateJoiner_NeverForCancelledClosedOrExplicitRuns()
    {
        var all = new RunAudience { Kind = AudienceKind.All };
        Assert.IsFalse(AudiencePlanner.ShouldAssignLateJoiner(FleetRun(all, cancelledAt: Now.AddMinutes(-1)), Now, new QueueRandom(0.0)));
        Assert.IsFalse(AudiencePlanner.ShouldAssignLateJoiner(FleetRun(all) with { WindowEnd = Now }, Now, new QueueRandom(0.0)));
        Assert.IsFalse(AudiencePlanner.ShouldAssignLateJoiner(FleetRun(new RunAudience { Kind = AudienceKind.Users, BattleTags = ["A#1"] }) with { WindowEnd = null }, Now, new QueueRandom(0.0)));
    }

    [TestCase(10, 3, 2, 5)]
    [TestCase(10, 8, 5, 0)]
    [TestCase(10, 10, 0, 0)]
    public void TopUpNeed(int count, int delivered, int pending, int expected) =>
        Assert.AreEqual(expected, AudiencePlanner.TopUpNeed(count, delivered, pending));
}
