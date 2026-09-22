using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
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
    // sha256("123456") — a public test vector, not a real credential.
    private const string Credential = "8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92";

    // Positive control: a declared field the fixture document carries, proving deserialisation actually ran.
    private const string MapName = "Legion TD";

    // The serialised `mapName` property itself, not just its value: `match.map` ("W3Champions/CustomGames/
    // Legion TD-94ec3bda.w3x") also contains "Legion TD" as a plain substring, so a bare Does.Contain(MapName)
    // would pass even if `mapName` itself were dropped and only `map` survived deserialisation.
    private const string MapNameProperty = "\"mapName\":\"" + MapName + "\"";

    // Any member whose name (or BSON element name) contains this fragment is rejected by the guard.
    private const string ForbiddenNameFragment = "floTvPassword";

    // The name the guard recommends for a client-facing FloTV flag, taken from the sample DTO below so the
    // literal cannot drift from what ADtoDeclaringTheRecommendedFlagPassesTheGuard actually declares. It
    // deliberately does not contain ForbiddenNameFragment, so following the guard's own advice can never turn
    // the guard red.
    private const string RecommendedFlagName = nameof(DtoWithTheRecommendedFlag.floTvProtected);

    // The API serialises with System.Text.Json under MVC's defaults: Program.cs registers AddControllers()
    // with neither AddNewtonsoftJson nor AddJsonOptions, and AddSignalR() uses the same JSON protocol.
    // Egress is therefore modelled with that serializer and those defaults.
    private static readonly JsonSerializerOptions ApiJsonOptions = new(JsonSerializerDefaults.Web);

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
        var served = ServeAsApiJson(matchEvent.match);
        Assert.That(served, Does.Contain(MapNameProperty),
            "positive control: the mapName field must have been deserialised (map also contains \"Legion TD\", so this checks mapName specifically)");
        Assert.That(served, Does.Not.Contain(Credential),
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

        Assert.That(matchEvent.match, Is.Not.Null);
        var served = ServeAsApiJson(matchEvent.match);
        Assert.That(served, Does.Contain(MapNameProperty),
            "positive control: the mapName field must have been deserialised (map also contains \"Legion TD\", so this checks mapName specifically)");
        Assert.That(served, Does.Not.Contain(Credential),
            "a credential-bearing matchmaking field must not survive deserialization");
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

        var served = ServeAsApiJson(matchup);
        Assert.That(served, Does.Contain(MapName), "positive control: the read model must carry the whitelisted fields");
        Assert.That(served, Does.Not.Contain(Credential),
            "Matchup.Create copies an explicit whitelist; keep it that way");
    }

    [TestCase(typeof(Match))]
    [TestCase(typeof(UnfinishedMatch))]
    public void TheEventDtosNeitherDeclareNorCouldExposeTheCredential(Type dtoType)
    {
        var offending = IngestVisibleNames(dtoType).Where(CarriesForbiddenName).ToList();

        Assert.That(offending, Is.Empty,
            $"{dtoType.Name} must never declare floTvPasswordSha256 — it is a credential-bearing field that " +
            "matchmaking writes into the match event documents, and this service must neither deserialise " +
            $"nor re-serve it. If a FloTV flag is needed on the client, add a boolean named {RecommendedFlagName} instead.");

        var attribute = dtoType.GetCustomAttribute<BsonIgnoreExtraElementsAttribute>();
        Assert.That(attribute?.IgnoreExtraElements, Is.True,
            $"{dtoType.Name} relies on [BsonIgnoreExtraElements] to discard undeclared matchmaking " +
            "fields; removing or disabling it turns unknown fields into deserialization failures and removes the guard.");
        Assert.That(BsonClassMap.LookupClassMap(dtoType).IgnoreExtraElements, Is.True,
            $"the driver's effective class map for {dtoType.Name} must ignore extra elements");
    }

    [Test]
    public void ADtoDeclaringTheRecommendedFlagPassesTheGuard()
    {
        var names = IngestVisibleNames(typeof(DtoWithTheRecommendedFlag)).ToList();

        Assert.That(names, Does.Contain(RecommendedFlagName),
            "the sample DTO must declare the recommended name exactly, or this test pins nothing");
        Assert.That(names.Where(CarriesForbiddenName), Is.Empty,
            $"the guard recommends '{RecommendedFlagName}'; that name must itself pass the guard");
    }

    [TestCase("floTvPasswordSha256")]
    [TestCase("FloTvPasswordSha256")]
    [TestCase("FLOTVPASSWORDSHA256")]
    [TestCase("_floTvPasswordSha256")]
    public void TheGuardRejectsTheCredentialFieldNameInAnyCasing(string name)
    {
        Assert.That(CarriesForbiddenName(name), Is.True);
    }

    private static string ServeAsApiJson<T>(T value) => JsonSerializer.Serialize(value, ApiJsonOptions);

    private static bool CarriesForbiddenName(string name) =>
        name.Contains(ForbiddenNameFragment, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every name under which a document element can enter or leave the DTO: the public instance
    /// properties (what the API serializer emits) plus every member the driver maps — public or private,
    /// property or field — by member name and by BSON element name. A private [BsonElement]-mapped member
    /// (the endTime idiom in MatchEventDtos.cs) has no public name at all, so the class map is the only
    /// place it shows up.
    /// </summary>
    private static IEnumerable<string> IngestVisibleNames(Type dtoType)
    {
        var classMap = BsonClassMap.LookupClassMap(dtoType);

        return dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)
            .Concat(classMap.AllMemberMaps.Select(m => m.MemberName))
            .Concat(classMap.AllMemberMaps.Select(m => m.ElementName))
            .Distinct(StringComparer.Ordinal);
    }

    [BsonIgnoreExtraElements]
    private sealed class DtoWithTheRecommendedFlag
    {
        public bool floTvProtected { get; set; }
    }

    private static BsonDocument MatchDocumentWithCredential() => new()
    {
        { "_id", "match-1" },
        { "id", "match-1" },
        { "state", 2 },
        { "season", 1 },
        { "gameMode", 6 },
        { "map", "W3Champions/CustomGames/Legion TD-94ec3bda.w3x" },
        { "mapName", MapName },
        { "mapId", 5811 },
        { "floTvMode", 1 },
        { "floTvPasswordSha256", Credential },
        { "players", new BsonArray() },
        { "teams", new BsonArray() },
    };
}
