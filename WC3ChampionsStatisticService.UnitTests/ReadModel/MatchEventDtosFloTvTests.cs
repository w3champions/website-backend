using System;
using System.Linq;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Matches;

namespace WC3ChampionsStatisticService.Tests.ReadModel;

/// <summary>
/// Pins the ingest-side boundary between the matchmaking match-event documents and this service.
/// <para>
/// matchmaking-service writes the whole Match object into the MatchStartedEvent / MatchFinishedEvent /
/// MatchCanceledEvent collections this service polls, and those documents carry fields that must never
/// be deserialised or re-served here — floTvPasswordSha256 is a credential-bearing field. Nothing strips
/// it on the way out; it simply never enters the C# object graph, because the DTOs do not declare it and
/// [BsonIgnoreExtraElements] discards undeclared fields. That is one choke point covering every surface
/// this service serialises from the object graph — far stronger than remembering to scrub it per
/// endpoint — but it is invisible, so these tests make it explicit and loud.
/// </para>
/// </summary>
[TestFixture]
public class MatchEventDtosFloTvTests
{
    private const string Credential = "8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92";

    [Test]
    public void MatchFinishedEvent_DropsFloTvPasswordSha256OnIngest()
    {
        var document = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            { "match", MatchDocumentWithCredential() },
        };

        var matchEvent = BsonSerializer.Deserialize<MatchFinishedEvent>(document);

        Assert.That(matchEvent.match, Is.Not.Null);
        Assert.That(JsonConvert.SerializeObject(matchEvent.match), Does.Not.Contain(Credential),
            "a credential-bearing matchmaking field must not survive deserialization");
    }

    [Test]
    public void MatchStartedEvent_DropsFloTvPasswordSha256OnIngest()
    {
        var document = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            { "match", MatchDocumentWithCredential() },
        };

        var matchEvent = BsonSerializer.Deserialize<MatchStartedEvent>(document);

        Assert.That(JsonConvert.SerializeObject(matchEvent.match), Does.Not.Contain(Credential));
    }

    [Test]
    public void TheReadModelBuiltFromAnEventCarriesNoCredential()
    {
        var matchEvent = BsonSerializer.Deserialize<MatchFinishedEvent>(new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            { "match", MatchDocumentWithCredential() },
            { "result", new BsonDocument { { "players", new BsonArray() } } },
        });

        var matchup = Matchup.Create(matchEvent);

        Assert.That(JsonConvert.SerializeObject(matchup), Does.Not.Contain(Credential),
            "Matchup.Create copies an explicit whitelist; keep it that way");
    }

    [TestCase(typeof(Match))]
    [TestCase(typeof(UnfinishedMatch))]
    public void TheEventDtosNeitherDeclareNorCouldExposeTheCredential(Type dtoType)
    {
        Assert.That(
            dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(p => p.Name.Contains("floTvPassword", StringComparison.OrdinalIgnoreCase)
                       || p.Name.Contains("FloTvPassword", StringComparison.Ordinal)),
            Is.False,
            $"{dtoType.Name} must never declare floTvPasswordSha256 — it is a credential-bearing field that " +
            "matchmaking writes into the match event documents, and this service must neither deserialise " +
            "nor re-serve it. If a FloTV flag is needed on the client, add a boolean like floTvPasswordProtected instead.");

        Assert.That(dtoType.GetCustomAttribute<BsonIgnoreExtraElementsAttribute>(), Is.Not.Null,
            $"{dtoType.Name} relies on [BsonIgnoreExtraElements] to discard undeclared matchmaking " +
            "fields; removing it turns unknown fields into deserialization failures and removes the guard.");
    }

    private static BsonDocument MatchDocumentWithCredential() => new()
    {
        { "_id", "match-1" },
        { "id", "match-1" },
        { "state", 2 },
        { "season", 1 },
        { "gameMode", 6 },
        { "map", "W3Champions/CustomGames/Legion TD-94ec3bda.w3x" },
        { "mapName", "Legion TD" },
        { "mapId", 5811 },
        { "floTvMode", 1 },
        { "floTvPasswordSha256", Credential },
        { "players", new BsonArray() },
        { "teams", new BsonArray() },
    };
}
