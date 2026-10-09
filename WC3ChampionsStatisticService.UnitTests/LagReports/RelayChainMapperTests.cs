using System;
using Google.Protobuf;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;
using W3ChampionsStatisticService.LagReports.FloControllerGrpc;
using static WC3ChampionsStatisticService.Tests.LagReports.RelayTelemetryTestData;

namespace WC3ChampionsStatisticService.Tests.LagReports;

[TestFixture]
public class RelayChainMapperTests
{
    private static readonly DateTime FetchedAt = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    private static RelaySeries GrpcSeries(string role, byte[] buckets, long closed = 9_000, string kind = "tcp") => new()
    {
        Role = role,
        Kind = kind,
        FirstSeenUnixMs = 1_000,
        ClosedUnixMs = closed,
        BucketsStartUnixMs = 1_500,
        Buckets = ByteString.CopyFrom(buckets),
        BucketCount = (uint)(buckets.Length / 19),
    };

    private static GetGameRelayTelemetryReply Reply(params RelayConnection[] connections)
    {
        var reply = new GetGameRelayTelemetryReply { GameId = 77, PlayerId = 5 };
        reply.Connections.AddRange(connections);
        return reply;
    }

    [Test]
    public void FromReply_MapsLegsClientFirstWithSeriesAndCloseLine()
    {
        var connection = new RelayConnection { ConnectedUnixMs = 1_234 };
        connection.Legs.Add(new RelayLeg
        {
            FromLabel = "client",
            ToLabel = "relay-a",
            Status = "measured",
            Far = GrpcSeries("haproxy_fe", Pack(Healthy(30))),
            Close = new RelayCloseLine { FcRttMs = 31, FcRttvarMs = 4, FcRetrans = 2, FcLost = 1, BcRttMs = 12, Term = "--", BytesIn = 10_000_000_000, BytesOut = 5, DurationMs = 60_000 },
        });
        connection.Legs.Add(new RelayLeg
        {
            FromLabel = "relay-a",
            ToLabel = "node-1",
            Status = "measured",
            Near = GrpcSeries("haproxy_be", Pack(Healthy(12))),
            Far = GrpcSeries("node_player", Pack(Healthy(13), Gap)),
        });

        var chain = RelayChainMapper.FromReply(Reply(connection), FetchedAt);

        Assert.That(chain.FetchedAt, Is.EqualTo(FetchedAt));
        Assert.That(chain.Connections, Has.Count.EqualTo(1));
        var c = chain.Connections[0];
        Assert.That(c.ConnectedUnixMs, Is.EqualTo(1_234));
        Assert.That(c.Legs, Has.Count.EqualTo(2));

        var clientLeg = c.Legs[0];
        Assert.That(clientLeg.FromLabel, Is.EqualTo("client"));
        Assert.That(clientLeg.ToLabel, Is.EqualTo("relay-a"));
        Assert.That(clientLeg.Near, Is.Null, "an unset proto message maps to null, not an empty series");
        Assert.That(clientLeg.Far.Role, Is.EqualTo("haproxy_fe"));
        Assert.That(clientLeg.Close.FcRttMs, Is.EqualTo(31));
        Assert.That(clientLeg.Close.BcRttMs, Is.EqualTo(12));
        Assert.That(clientLeg.Close.Term, Is.EqualTo("--"));
        Assert.That(clientLeg.Close.BytesIn, Is.EqualTo(10_000_000_000UL));
        Assert.That(clientLeg.Close.DurationMs, Is.EqualTo(60_000));

        var nodeLeg = c.Legs[1];
        Assert.That(nodeLeg.Close, Is.Null);
        Assert.That(nodeLeg.Near.Role, Is.EqualTo("haproxy_be"));
        Assert.That(nodeLeg.Far.Kind, Is.EqualTo("tcp"));
        Assert.That(nodeLeg.Far.FirstSeenUnixMs, Is.EqualTo(1_000));
        Assert.That(nodeLeg.Far.ClosedUnixMs, Is.EqualTo(9_000));
        Assert.That(nodeLeg.Far.BucketsStartUnixMs, Is.EqualTo(1_500));
        Assert.That(nodeLeg.Far.BucketSecs, Is.EqualTo(5));
        Assert.That(nodeLeg.Far.BucketCount, Is.EqualTo(2));
        Assert.That(nodeLeg.Far.Buckets.SrttMaxMs, Is.EqualTo(new ushort?[] { 13, null }));
    }

    [TestCase(RelayLegStatus.Measured)]
    [TestCase(RelayLegStatus.OneSided)]
    [TestCase(RelayLegStatus.PendingClose)]
    [TestCase(RelayLegStatus.UnmeasuredNoFloNode)]
    [TestCase(RelayLegStatus.UnmeasuredPortRewritten)]
    [TestCase(RelayLegStatus.NodeTooOld)]
    [TestCase(RelayLegStatus.Expired)]
    [TestCase(RelayLegStatus.NodeUnavailable)]
    [TestCase(RelayLegStatus.HopLimit)]
    [TestCase("some_future_status")]
    public void FromReply_KeepsEveryStatusVerbatim(string status)
    {
        var connection = new RelayConnection { ConnectedUnixMs = 1 };
        connection.Legs.Add(new RelayLeg { FromLabel = "relay-b", ToLabel = "node-1", Status = status, Far = GrpcSeries("node_player", Pack(Healthy())) });

        var leg = RelayChainMapper.FromReply(Reply(connection), FetchedAt).Connections[0].Legs[0];

        Assert.That(leg.Status, Is.EqualTo(status));
        Assert.That(leg.Far.BucketCount, Is.EqualTo(1));
    }

    [Test]
    public void FromReply_UnmeasuredLegHasNoSeries()
    {
        var connection = new RelayConnection { ConnectedUnixMs = 1 };
        connection.Legs.Add(new RelayLeg { FromLabel = "client", ToLabel = "korea-central-3", Status = RelayLegStatus.UnmeasuredNoFloNode });

        var leg = RelayChainMapper.FromReply(Reply(connection), FetchedAt).Connections[0].Legs[0];

        Assert.That(leg.Near, Is.Null);
        Assert.That(leg.Far, Is.Null);
        Assert.That(leg.Close, Is.Null);
    }

    [Test]
    public void FromReply_QuicConnectionHasOnlyTheNodeSeries()
    {
        var connection = new RelayConnection { ConnectedUnixMs = 1 };
        connection.Legs.Add(new RelayLeg
        {
            FromLabel = "client",
            ToLabel = "node-1",
            Status = RelayLegStatus.Measured,
            Far = GrpcSeries("node_quic", Pack(new B(55, 0xFFFF, 0, 1, 0xFFFF, 10, 20, 0)), kind: "quic"),
        });

        var far = RelayChainMapper.FromReply(Reply(connection), FetchedAt).Connections[0].Legs[0].Far;

        Assert.That(far.Kind, Is.EqualTo("quic"));
        Assert.That(far.Buckets.RttvarMaxMs, Is.EqualTo(new ushort?[] { null }));
    }

    [Test]
    public void FromReply_TrailingPartialBucketIsDropped()
    {
        var bytes = Pack(Healthy(), Healthy());
        var truncated = bytes[..(bytes.Length - 3)];
        var connection = new RelayConnection { ConnectedUnixMs = 1 };
        connection.Legs.Add(new RelayLeg { FromLabel = "client", ToLabel = "node-1", Status = "measured", Far = GrpcSeries("node_player", truncated) });

        var far = RelayChainMapper.FromReply(Reply(connection), FetchedAt).Connections[0].Legs[0].Far;

        Assert.That(far.BucketCount, Is.EqualTo(1));
        Assert.That(far.PackedBuckets, Has.Length.EqualTo(19));
    }

    [Test]
    public void FromReply_NoConnectionsGivesAnEmptyChain()
    {
        var chain = RelayChainMapper.FromReply(Reply(), FetchedAt);

        Assert.That(chain, Is.Not.Null);
        Assert.That(chain.Connections, Is.Empty);
    }
}
