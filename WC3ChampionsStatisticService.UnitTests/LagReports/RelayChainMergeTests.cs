using System;
using System.Linq;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;
using static WC3ChampionsStatisticService.Tests.LagReports.RelayTelemetryTestData;

namespace WC3ChampionsStatisticService.Tests.LagReports;

[TestFixture]
public class RelayChainMergeTests
{
    private static int TotalBytes(PlayerRelayChain chain) => chain.Connections
        .SelectMany(c => c.Legs)
        .SelectMany(l => new[] { l.Near, l.Far })
        .Where(s => s != null)
        .Sum(s => s.PackedBuckets.Length);

    // ── NeedsRefresh ──────────────────────────────────────────────────

    [Test]
    public void NeedsRefresh_MissingOrEmptyChain()
    {
        Assert.That(RelayChainMerge.NeedsRefresh(null), Is.True);
        Assert.That(RelayChainMerge.NeedsRefresh(Chain()), Is.True);
    }

    [Test]
    public void NeedsRefresh_ClosedMeasuredChainIsFinal()
    {
        Assert.That(RelayChainMerge.NeedsRefresh(Chain(RelayedConnection(1, 10))), Is.False);
    }

    [TestCase(RelayLegStatus.PendingClose, true)]
    [TestCase(RelayLegStatus.NodeUnavailable, true)]
    [TestCase(RelayLegStatus.OneSided, true)]
    [TestCase(RelayLegStatus.UnmeasuredNoFloNode, true)]
    [TestCase(RelayLegStatus.UnmeasuredPortRewritten, true)]
    [TestCase(RelayLegStatus.NodeTooOld, true)]
    [TestCase(RelayLegStatus.HopLimit, true)]
    [TestCase("unmeasured_quic_relay", true)]
    [TestCase("a_status_from_the_future", true)]
    [TestCase(null, true)]
    [TestCase(RelayLegStatus.Measured, false)]
    [TestCase(RelayLegStatus.Expired, false)]
    public void NeedsRefresh_ByLegStatus(string status, bool expected)
    {
        Assert.That(RelayChainMerge.NeedsRefresh(Chain(RelayedConnection(1, 10, status))), Is.EqualTo(expected));
    }

    [Test]
    public void NeedsRefresh_MeasuredClientLegDoesNotMakeAChainFinalWhileALaterLegCanImprove()
    {
        var connection = RelayedConnection(1, 10);
        connection.Legs[0].Close = new RelayCloseLineData();
        connection.Legs[1].Status = RelayLegStatus.NodeTooOld;

        Assert.That(RelayChainMerge.NeedsRefresh(Chain(connection)), Is.True);
    }

    [Test]
    public void NeedsRefresh_AnyUnfinishedConnectionKeepsTheChainOpen()
    {
        var chain = Chain(RelayedConnection(1, 10), RelayedConnection(2, 10, RelayLegStatus.OneSided));

        Assert.That(RelayChainMerge.NeedsRefresh(chain), Is.True);
    }

    [Test]
    public void NeedsRefresh_MeasuredRelayedLegNeedsItsCloseLine()
    {
        // Only the leg into the game node has no close line; a leg into a relay is measured
        // from the relay's close line, so a measured one without it is incomplete.
        var connection = RelayedConnection(1, 10);
        connection.Legs[0].Close = new RelayCloseLineData();
        Assert.That(RelayChainMerge.NeedsRefresh(Chain(connection)), Is.False);

        connection.Legs[0].Close = null;
        Assert.That(RelayChainMerge.NeedsRefresh(Chain(connection)), Is.True);
    }

    [Test]
    public void NeedsRefresh_ConnectionWithoutLegsIsNotFinal()
    {
        Assert.That(RelayChainMerge.NeedsRefresh(Chain(new RelayConnectionData { ConnectedUnixMs = 1 })), Is.True);
    }

    [Test]
    public void NeedsRefresh_DirectConnectionIsFinalWithoutCloseLine()
    {
        var direct = new RelayConnectionData
        {
            ConnectedUnixMs = 1,
            Legs = [Leg("client", "node-1", RelayLegStatus.Measured, null, Series("node_player", 10))],
        };
        Assert.That(RelayChainMerge.NeedsRefresh(Chain(direct)), Is.False);

        direct.Legs[0].Far.ClosedUnixMs = 0;
        Assert.That(RelayChainMerge.NeedsRefresh(Chain(direct)), Is.True);
    }

    [Test]
    public void IsStillClosing_FalseForAFinalChainWithAnOpenSeriesOnAnExpiredLeg()
    {
        var connection = RelayedConnection(1, 10, RelayLegStatus.Expired);
        connection.Legs[1].Far.ClosedUnixMs = 0;

        Assert.That(RelayChainMerge.IsStillClosing(Chain(connection)), Is.False);
    }

    [Test]
    public void IsStillClosing_IgnoresAnOpenSeriesOnADifferentFinalConnection()
    {
        var expired = RelayedConnection(1, 10, RelayLegStatus.Expired);
        expired.Legs[1].Far.ClosedUnixMs = 0;
        var stale = RelayedConnection(2, 10, RelayLegStatus.OneSided);

        Assert.That(RelayChainMerge.NeedsRefresh(Chain(expired, stale)), Is.True);
        Assert.That(RelayChainMerge.IsStillClosing(Chain(expired, stale)), Is.False);
    }

    [TestCase(RelayLegStatus.OneSided)]
    [TestCase(RelayLegStatus.PendingClose)]
    public void Merge_FreshExpiredOrUnavailableReplacesAnOldNonMeasuredStatus(string oldStatus)
    {
        var existing = Chain(RelayedConnection(1, 10, oldStatus));
        var fresh = Chain(RelayedConnection(1, 10, RelayLegStatus.Expired));
        var merged = RelayChainMerge.Merge(existing, fresh);
        Assert.That(merged.Connections[0].Legs.Select(l => l.Status), Has.All.EqualTo(RelayLegStatus.Expired));

        fresh = Chain(RelayedConnection(1, 10, RelayLegStatus.NodeUnavailable));
        merged = RelayChainMerge.Merge(existing, fresh);
        Assert.That(RelayChainMerge.IsStillClosing(merged), Is.True);
    }

    [Test]
    public void Merge_MeasuredLegMissingItsCloseLineYieldsToAFreshNodeUnavailable()
    {
        var old = RelayedConnection(1, 10);
        old.Legs[0].Close = null;
        var fresh = RelayedConnection(1, 10, RelayLegStatus.NodeUnavailable);
        fresh.Legs[0].Close = null;

        var merged = RelayChainMerge.Merge(Chain(old), Chain(fresh));

        Assert.That(RelayChainMerge.IsStillClosing(merged), Is.True);
    }

    [Test]
    public void Merge_ExpiredLegStaysExpiredWhenARefreshCannotReachItsNode()
    {
        var existing = Chain(RelayedConnection(1, 10, RelayLegStatus.Expired));
        var fresh = Chain(RelayedConnection(1, 10, RelayLegStatus.NodeUnavailable));

        var merged = RelayChainMerge.Merge(existing, fresh);

        Assert.That(RelayChainMerge.NeedsRefresh(merged), Is.False);
    }

    [TestCase(RelayLegStatus.NodeUnavailable)]
    [TestCase(RelayLegStatus.NodeTooOld)]
    public void Merge_MeasuredLegWithoutBucketsStaysFinalOverALaterLossOfAccess(string freshStatus)
    {
        var old = RelayedConnection(1, 0);
        var fresh = RelayedConnection(1, 0, freshStatus);

        var merged = RelayChainMerge.Merge(Chain(old), Chain(fresh));

        Assert.That(RelayChainMerge.NeedsRefresh(merged), Is.False);
    }

    [Test]
    public void IsStillClosing_IgnoresAnOpenSeriesOnAFinalLegOfANonFinalConnection()
    {
        var connection = RelayedConnection(1, 10);
        connection.Legs[0].Status = RelayLegStatus.OneSided;
        connection.Legs[1].Status = RelayLegStatus.Expired;
        connection.Legs[1].Far.ClosedUnixMs = 0;

        Assert.That(RelayChainMerge.NeedsRefresh(Chain(connection)), Is.True);
        Assert.That(RelayChainMerge.IsStillClosing(Chain(connection)), Is.False);
    }

    [Test]
    public void IsStillClosing_FreshStaleVerdictOverARetainedOpenSeriesWaitsForTheNextTrigger()
    {
        var existing = Chain(RelayedConnection(1, 30, RelayLegStatus.PendingClose, closedUnixMs: 0));
        var fresh = Chain(RelayedConnection(1, 5, RelayLegStatus.NodeTooOld));

        var merged = RelayChainMerge.Merge(existing, fresh);

        Assert.That(RelayChainMerge.NeedsRefresh(merged), Is.True);
        Assert.That(RelayChainMerge.IsStillClosing(merged), Is.False);
    }

    [Test]
    public void IsStillClosing_OnlyTransientStates()
    {
        Assert.That(RelayChainMerge.IsStillClosing(Chain(RelayedConnection(1, 10, RelayLegStatus.PendingClose))), Is.True);
        Assert.That(RelayChainMerge.IsStillClosing(Chain(RelayedConnection(1, 10, RelayLegStatus.NodeUnavailable))), Is.True);
        Assert.That(RelayChainMerge.IsStillClosing(Chain(RelayedConnection(1, 10, RelayLegStatus.NodeTooOld))), Is.False);
        Assert.That(RelayChainMerge.IsStillClosing(Chain(RelayedConnection(1, 10, "unmeasured_quic_relay"))), Is.False);
    }

    [Test]
    public void Merge_StoresAnUnknownStatusVerbatim()
    {
        var merged = RelayChainMerge.Merge(null, Chain(RelayedConnection(1, 10, "unmeasured_quic_relay")));

        Assert.That(merged.Connections[0].Legs.Select(l => l.Status), Has.All.EqualTo("unmeasured_quic_relay"));
    }

    [Test]
    public void NeedsRefresh_OpenNodeSeries()
    {
        var connection = RelayedConnection(1, 10);
        connection.Legs[1].Far.ClosedUnixMs = 0;

        Assert.That(RelayChainMerge.NeedsRefresh(Chain(connection)), Is.True);
    }

    [Test]
    public void NeedsRefresh_OpenRelaySeriesAloneDoesNotTrigger()
    {
        // Only the node's series marks the connection as still open; a relay's view of an
        // already-closed connection can lag behind and must not cause endless refreshes.
        var connection = RelayedConnection(1, 10);
        connection.Legs[0].Far.ClosedUnixMs = 0;

        Assert.That(RelayChainMerge.NeedsRefresh(Chain(connection)), Is.False);
    }

    // ── Merge ─────────────────────────────────────────────────────────

    [Test]
    public void Merge_WithoutExistingReturnsFresh()
    {
        var fresh = Chain(RelayedConnection(1, 10));

        var merged = RelayChainMerge.Merge(null, fresh);

        Assert.That(merged.Connections, Has.Count.EqualTo(1));
        Assert.That(merged.Connections[0].Legs[1].Far.BucketCount, Is.EqualTo(10));
    }

    [Test]
    public void Merge_RefreshExtendsAPendingCloseChain()
    {
        var existing = Chain(RelayedConnection(1, 10, RelayLegStatus.PendingClose, closedUnixMs: 0));
        var freshAt = new DateTime(2026, 10, 9, 13, 0, 0, DateTimeKind.Utc);
        var fresh = Chain(RelayedConnection(1, 30));
        fresh.FetchedAt = freshAt;

        var merged = RelayChainMerge.Merge(existing, fresh);

        Assert.That(merged.FetchedAt, Is.EqualTo(freshAt));
        Assert.That(merged.Connections, Has.Count.EqualTo(1));
        Assert.That(merged.Connections[0].Legs.Select(l => l.Status), Is.All.EqualTo(RelayLegStatus.Measured));
        Assert.That(merged.Connections[0].Legs[1].Far.BucketCount, Is.EqualTo(30));
    }

    [Test]
    public void Merge_EqualDataPrefersTheFreshLegSoStatusAndCloseLineAdvance()
    {
        var existing = Chain(RelayedConnection(1, 10, RelayLegStatus.PendingClose));
        var fresh = Chain(RelayedConnection(1, 10));
        fresh.Connections[0].Legs[0].Close = new RelayCloseLineData { FcRttMs = 30, Term = "--" };

        var merged = RelayChainMerge.Merge(existing, fresh);

        Assert.That(merged.Connections[0].Legs[0].Status, Is.EqualTo(RelayLegStatus.Measured));
        Assert.That(merged.Connections[0].Legs[0].Close.FcRttMs, Is.EqualTo(30));
    }

    [Test]
    public void Merge_ExpiredRefreshKeepsTheEarlierData()
    {
        var existing = Chain(RelayedConnection(1, 30, RelayLegStatus.PendingClose, closedUnixMs: 0));
        var fresh = Chain(RelayedConnection(1, 0, RelayLegStatus.Expired));

        var merged = RelayChainMerge.Merge(existing, fresh);

        var legs = merged.Connections[0].Legs;
        Assert.That(legs.Select(l => l.Status), Is.All.EqualTo(RelayLegStatus.Expired));
        Assert.That(legs[1].Far.BucketCount, Is.EqualTo(30));
    }

    [Test]
    public void Merge_MergesPerLegWhenTheLabelsMatch()
    {
        var existing = Chain(RelayedConnection(1, 30, RelayLegStatus.PendingClose, closedUnixMs: 0));
        var fresh = Chain(RelayedConnection(1, 40));
        // The relay restarted: its leg came back expired and empty, the node leg grew.
        fresh.Connections[0].Legs[0] = Leg("client", "relay-a", RelayLegStatus.Expired, null, null);

        var merged = RelayChainMerge.Merge(existing, fresh);

        var legs = merged.Connections[0].Legs;
        Assert.That(legs[0].Status, Is.EqualTo(RelayLegStatus.Expired));
        Assert.That(legs[0].Far.BucketCount, Is.EqualTo(30));
        Assert.That(legs[1].Status, Is.EqualTo(RelayLegStatus.Measured));
        Assert.That(legs[1].Far.BucketCount, Is.EqualTo(40));
    }

    [Test]
    public void Merge_KeepsEachEndOfALegFromWhicheverFetchHasMoreOfIt()
    {
        var existing = Chain(RelayedConnection(1, 30, RelayLegStatus.PendingClose, closedUnixMs: 0));
        existing.Connections[0].Legs[1].Far = null;
        var fresh = Chain(RelayedConnection(1, 40));
        fresh.Connections[0].Legs[1].Near = null;
        fresh.Connections[0].Legs[1].Close = new RelayCloseLineData { BcRttMs = 9 };

        var merged = RelayChainMerge.Merge(existing, fresh);

        var leg = merged.Connections[0].Legs[1];
        Assert.That(leg.Near.BucketCount, Is.EqualTo(30), "the relay's side survives a fetch that lost it");
        Assert.That(leg.Far.BucketCount, Is.EqualTo(40));
        Assert.That(leg.Status, Is.EqualTo(RelayLegStatus.Measured));
        Assert.That(leg.Close.BcRttMs, Is.EqualTo(9));
    }

    [Test]
    public void Merge_DifferentPathKeepsTheConnectionWithMoreData()
    {
        var existing = Chain(RelayedConnection(1, 30, RelayLegStatus.PendingClose, closedUnixMs: 0));
        var shorter = new RelayConnectionData
        {
            ConnectedUnixMs = 1,
            Legs = [Leg("relay-a", "node-1", RelayLegStatus.NodeUnavailable, null, Series("node_player", 2))],
        };

        var merged = RelayChainMerge.Merge(existing, Chain(shorter));

        Assert.That(merged.Connections[0].Legs, Has.Count.EqualTo(2));
        Assert.That(merged.Connections[0].Legs[1].Far.BucketCount, Is.EqualTo(30));
    }

    [Test]
    public void Merge_KeepsConnectionsTheRefreshNoLongerReturns()
    {
        var existing = Chain(RelayedConnection(1, 10), RelayedConnection(2, 10));
        var fresh = Chain(RelayedConnection(2, 12), RelayedConnection(3, 5));

        var merged = RelayChainMerge.Merge(existing, fresh);

        Assert.That(merged.Connections.Select(c => c.ConnectedUnixMs), Is.EqualTo(new long[] { 1, 2, 3 }));
        Assert.That(merged.Connections[1].Legs[1].Far.BucketCount, Is.EqualTo(12));
    }

    [Test]
    public void Merge_EmptyRefreshDropsNothing()
    {
        var existing = Chain(RelayedConnection(1, 10));

        var merged = RelayChainMerge.Merge(existing, Chain());

        Assert.That(merged.Connections, Has.Count.EqualTo(1));
        Assert.That(merged.Connections[0].Legs[1].Far.BucketCount, Is.EqualTo(10));
    }

    [Test]
    public void Merge_DoesNotMutateItsInputs()
    {
        var existing = Chain(RelayedConnection(1, 10, RelayLegStatus.PendingClose));
        var fresh = Chain(RelayedConnection(1, 10_000));

        RelayChainMerge.Merge(existing, fresh);

        Assert.That(existing.Connections[0].Legs[0].Status, Is.EqualTo(RelayLegStatus.PendingClose));
        Assert.That(fresh.Connections[0].Legs[1].Far.BucketCount, Is.EqualTo(10_000));
        Assert.That(fresh.Connections[0].Legs[1].Far.BucketSecs, Is.EqualTo(5));
    }

    // ── Bounds ────────────────────────────────────────────────────────

    [Test]
    public void Merge_KeepsOnlyTheMostRecentConnections()
    {
        var fresh = Chain(Enumerable.Range(1, RelayChainMerge.MaxConnectionsPerPlayer + 3)
            .Select(i => RelayedConnection(i, 1))
            .Reverse()
            .ToArray());

        var merged = RelayChainMerge.Merge(null, fresh);

        Assert.That(merged.Connections, Has.Count.EqualTo(RelayChainMerge.MaxConnectionsPerPlayer));
        Assert.That(merged.Connections.First().ConnectedUnixMs, Is.EqualTo(4));
        Assert.That(merged.Connections.Last().ConnectedUnixMs, Is.EqualTo(RelayChainMerge.MaxConnectionsPerPlayer + 3));
    }

    [Test]
    public void Merge_DownsamplesToStayWithinThePerPlayerBudget()
    {
        // A 90-minute game relayed over one hop: three 1,080-bucket series, ~61 KB raw.
        var fresh = Chain(RelayedConnection(1, 1_080));

        var merged = RelayChainMerge.Merge(null, fresh);

        Assert.That(TotalBytes(merged), Is.LessThanOrEqualTo(RelayChainMerge.MaxBucketBytesPerPlayer));
        var far = merged.Connections[0].Legs[1].Far;
        Assert.That(far.BucketSecs, Is.GreaterThan(5));
        Assert.That(far.BucketCount * far.BucketSecs, Is.GreaterThanOrEqualTo(1_080 * 5), "the whole game stays covered");
    }

    [Test]
    public void Merge_TypicalRelayedGameIsStoredAtFullResolution()
    {
        // 20 minutes over one relay (three series of 240 buckets) fits as is.
        var merged = RelayChainMerge.Merge(null, Chain(RelayedConnection(1, 240)));

        Assert.That(merged.Connections[0].Legs.SelectMany(l => new[] { l.Near, l.Far }).Where(s => s != null).Select(s => s.BucketSecs), Is.All.EqualTo(5));
    }
}
