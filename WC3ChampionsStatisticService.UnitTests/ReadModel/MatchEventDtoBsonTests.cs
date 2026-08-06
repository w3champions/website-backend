using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Domain.MatchmakingService;

namespace WC3ChampionsStatisticService.Tests.ReadModel;

/// <summary>
/// Pins the BSON contract of the match event DTOs against the shapes that actually exist in the
/// production event collections. Old documents written by the (JavaScript) matchmaking service
/// store <c>players[].race</c> as an explicit null. The C# driver refuses to deserialize null into
/// the non-nullable <see cref="Race"/> enum ("Cannot deserialize a 'Race' from BsonType 'Null'"),
/// and because that blows up inside <c>MatchEventRepository.Load</c> — before any per-event guard
/// in the read model handlers can run — a single such document wedges a handler permanently.
/// </summary>
[TestFixture]
public class MatchEventDtoBsonTests
{
    private static BsonDocument PlayerDocument(string battleTag, BsonValue race) => new()
    {
        { "battleTag", battleTag },
        { "race", race },
    };

    private static BsonDocument MatchDocument(BsonArray players) => new()
    {
        { "_id", "match-1" },
        { "state", (int)EMatchState.CANCELED },
        { "season", 1 },
        { "players", players },
    };

    [Test]
    public void MatchCanceledEvent_WithNullPlayerRace_DeserializesToRnD()
    {
        var document = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            {
                "match", MatchDocument([
                    PlayerDocument("peter#123", BsonNull.Value),
                    PlayerDocument("wolf#456", (int)Race.HU),
                ])
            },
        };

        var matchEvent = BsonSerializer.Deserialize<MatchCanceledEvent>(document);

        Assert.That(matchEvent.match.players[0].race, Is.EqualTo(Race.RnD),
            "A BSON null race must map to the enum's default (RnD), not throw");
        Assert.That(matchEvent.match.players[1].race, Is.EqualTo(Race.HU),
            "A concrete race must still deserialize unchanged");
    }

    [Test]
    public void MatchStartedEvent_WithNullPlayerRace_DeserializesToRnD()
    {
        // MatchStartedEvent carries UnfinishedMatchPlayer directly (PlayerMMrChange derives from it),
        // so this covers the base class rather than the derived one.
        var document = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            {
                "match", MatchDocument([PlayerDocument("peter#123", BsonNull.Value)])
            },
        };

        var matchEvent = BsonSerializer.Deserialize<MatchStartedEvent>(document);

        Assert.That(matchEvent.match.players[0].race, Is.EqualTo(Race.RnD));
    }

    [Test]
    public void PlayerRace_RoundTripsThroughBson()
    {
        // Guards the happy path: the null tolerance must not change how a real race is stored or read.
        var player = new UnfinishedMatchPlayer { battleTag = "peter#123", race = Race.NE };

        var roundTripped = BsonSerializer.Deserialize<UnfinishedMatchPlayer>(player.ToBsonDocument());

        Assert.That(roundTripped.race, Is.EqualTo(Race.NE));
        Assert.That(player.ToBsonDocument().Contains("race"), Is.True,
            "The race must still be persisted under the 'race' element name");
    }

    [Test]
    public void Match_WithNullEndTime_DeserializesToZero()
    {
        // Regression guard for the pre-existing private-backing-property idiom that the race fix mirrors.
        var document = new BsonDocument
        {
            { "_id", "match-1" },
            { "state", (int)EMatchState.FINISHED },
            { "endTime", BsonNull.Value },
        };

        var match = BsonSerializer.Deserialize<Match>(document);

        Assert.That(match.endTime, Is.EqualTo(0));
    }
}
