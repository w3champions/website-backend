using System.Linq;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;
using static WC3ChampionsStatisticService.Tests.LagReports.RelayTelemetryTestData;

namespace WC3ChampionsStatisticService.Tests.LagReports;

/// <summary>
/// Renders the per-player relay-chain update without a database: the write must target only
/// the entries of one verified flo player, so an early leaver's chain never touches anyone else's.
/// </summary>
[TestFixture]
public class LagReportRelayChainUpdateTests
{
    private static readonly RenderArgs<LagReport> Args =
        new(BsonSerializer.LookupSerializer<LagReport>(), BsonSerializer.SerializerRegistry);

    [Test]
    public void Update_SetsOnlyTheMatchingPlayersChainAsBinData()
    {
        var chain = Chain(RelayedConnection(1, 2));

        var (update, options) = LagReportRepository.BuildRelayChainUpdate(5, ["A#1", "a#1"], chain);

        var rendered = update.Render(Args).AsBsonDocument;
        var set = rendered["$set"].AsBsonDocument;
        Assert.That(set.Names, Does.Contain("Players.$[p].RelayChain"));
        Assert.That(set.Names, Does.Contain("UpdatedAt"));

        var series = set["Players.$[p].RelayChain"]["Connections"][0]["Legs"][1]["Far"].AsBsonDocument;
        Assert.That(series["Buckets"].BsonType, Is.EqualTo(BsonType.Binary));
        Assert.That(series["Buckets"].AsByteArray, Has.Length.EqualTo(38));
        Assert.That(series.Names, Does.Not.Contain("BucketCount"));
        Assert.That(series.Names.Any(n => n.Contains("SrttMaxMs")), Is.False);

        var filter = options.ArrayFilters.Single()
            .Render(BsonDocumentSerializer.Instance, BsonSerializer.SerializerRegistry);
        Assert.That(filter, Is.EqualTo(new BsonDocument { { "p.FloPlayerId", 5 }, { "p.BattleTag", new BsonDocument("$in", new BsonArray { "A#1", "a#1" }) } }));
    }

    [Test]
    public void StoredChain_RoundTripsThroughBson()
    {
        var player = new LagReportPlayer { BattleTag = "A#1", FloPlayerId = 5, RelayChain = Chain(RelayedConnection(7, 3)) };

        var back = BsonSerializer.Deserialize<LagReportPlayer>(player.ToBsonDocument());

        Assert.That(back.FloPlayerId, Is.EqualTo(5));
        var far = back.RelayChain.Connections[0].Legs[1].Far;
        Assert.That(far.BucketCount, Is.EqualTo(3));
        Assert.That(far.PackedBuckets, Is.EqualTo(PackHealthy(3)));
        Assert.That(back.RelayChain.Connections[0].ConnectedUnixMs, Is.EqualTo(7));
    }

    [Test]
    public void LegacyPlayerDocument_DeserializesWithoutRelayFields()
    {
        var legacy = new BsonDocument { { "BattleTag", "Old#1" }, { "IsExplicit", true } };

        var player = BsonSerializer.Deserialize<LagReportPlayer>(legacy);

        Assert.That(player.FloPlayerId, Is.Null);
        Assert.That(player.RelayChain, Is.Null);
    }
}
