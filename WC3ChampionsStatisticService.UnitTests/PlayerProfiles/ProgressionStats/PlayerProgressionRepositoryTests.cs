using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles.ProgressionStats;

[TestFixture]
public class PlayerProgressionRepositoryTests
{
    private MongoDbRunner _runner;
    private MongoClient _mongoClient;
    private PlayerProgressionRepository _repository;

    [SetUp]
    public void Setup()
    {
        _runner = MongoDbRunner.Start();
        _mongoClient = new MongoClient(_runner.ConnectionString);
        _repository = new PlayerProgressionRepository(_mongoClient);
    }

    [TearDown]
    public void TearDown()
    {
        _runner.Dispose();
    }

    private static PlayerProgression Make(string battleTag, int season, int league, int division, int points)
    {
        var id = new BattleTagIdCombined(
            new List<PlayerId> { PlayerId.Create(battleTag) },
            GateWay.Europe, GameMode.GM_1v1, season, Race.HU);
        var p = PlayerProgression.Create(id);
        p.RecordRank(league, division, points, null);
        return p;
    }

    private IMongoCollection<PlayerProgression> Collection() =>
        _mongoClient
            .GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<PlayerProgression>("PlayerProgression");

    [Test]
    public async Task Upsert_ThenLoad_RoundTrips()
    {
        var p = Make("peter#123", 2, 3, 2, 50);

        await _repository.UpsertProgression(p);
        var loaded = await _repository.LoadProgression(p.Id);

        Assert.IsNotNull(loaded);
        Assert.AreEqual(3, loaded.League);
        Assert.AreEqual(2, loaded.Division);
        Assert.AreEqual(50, loaded.Points);
        Assert.AreEqual(GameMode.GM_1v1, loaded.GameMode);
        Assert.AreEqual("peter#123", loaded.PlayerIds[0].BattleTag);
    }

    [Test]
    public async Task Upsert_Twice_KeepsSingleDocWithLatest()
    {
        var p = Make("peter#123", 2, 3, 2, 50);
        await _repository.UpsertProgression(p);

        var updated = Make("peter#123", 2, 4, 1, 10);
        await _repository.UpsertProgression(updated);

        var loaded = await _repository.LoadProgression(p.Id);
        Assert.AreEqual(4, loaded.League);
        Assert.AreEqual(1, loaded.Division);
        Assert.AreEqual(10, loaded.Points);
        Assert.AreEqual(1, await Collection().CountDocumentsAsync(FilterDefinition<PlayerProgression>.Empty));
    }

    [Test]
    public async Task LoadProgression_MissingId_ReturnsNull()
    {
        var loaded = await _repository.LoadProgression("nope");
        Assert.IsNull(loaded);
    }

    [Test]
    public async Task LoadProgressions_ReturnsMatchingDocs_OmitsMissing()
    {
        var a = Make("a#1", 2, 3, 2, 50);
        var b = Make("b#2", 2, 4, 1, 10);
        await _repository.UpsertProgression(a);
        await _repository.UpsertProgression(b);

        var loaded = await _repository.LoadProgressions(new List<string> { a.Id, b.Id, "missing#9" });

        Assert.AreEqual(2, loaded.Count);
        CollectionAssert.AreEquivalent(new[] { a.Id, b.Id }, loaded.Select(p => p.Id).ToList());
    }

    [Test]
    public async Task LoadProgressions_EmptyInput_ReturnsEmpty()
    {
        var loaded = await _repository.LoadProgressions(new List<string>());
        Assert.IsEmpty(loaded);
    }

    private static PlayerProgression MakeFor(
        string battleTag, int season, GameMode gameMode, Race? race, int league, int division, int points)
    {
        var id = new BattleTagIdCombined(
            new List<PlayerId> { PlayerId.Create(battleTag) },
            GateWay.Europe, gameMode, season, race);
        var p = PlayerProgression.Create(id);
        p.RecordRank(league, division, points, null);
        return p;
    }

    // ── C1: index ────────────────────────────────────────────────────────────

    [Test]
    public async Task EnsureIndexes_creates_league_query_index()
    {
        await _repository.EnsureIndexesAsync();

        var indexes = await (await Collection().Indexes.ListAsync()).ToListAsync();
        var indexDump = $"count={indexes.Count}; " + string.Join(" | ", indexes.Select(i => i.ToJson()));

        // Compound index backing LoadPlayersByProgressionLeague:
        // Season + GameMode + League + Division + Race (eq filters) then Points desc (sort).
        Assert.That(
            indexes.Any(i => i.GetValue("name", BsonString.Empty).AsString
                == "Season_1_GameMode_1_League_1_Division_1_Race_1_Points_-1"),
            Is.True,
            "league-query compound index missing — got: " + indexDump);
    }

    [Test]
    public async Task EnsureIndexes_is_idempotent()
    {
        await _repository.EnsureIndexesAsync();
        Assert.DoesNotThrowAsync(async () => await _repository.EnsureIndexesAsync());
    }

    // ── C2: query ────────────────────────────────────────────────────────────

    [Test]
    public async Task LoadPlayersByProgressionLeague_FiltersBySeasonModeLeagueDivision_PointsDesc()
    {
        // Adept I (league 2, division 1): three players, different points.
        await _repository.UpsertProgression(MakeFor("low#1", 2, GameMode.GM_1v1, null, 2, 1, 10));
        await _repository.UpsertProgression(MakeFor("high#2", 2, GameMode.GM_1v1, null, 2, 1, 90));
        await _repository.UpsertProgression(MakeFor("mid#3", 2, GameMode.GM_1v1, null, 2, 1, 50));
        // Noise that must be excluded: Adept II, Diamond I, another mode, another season.
        await _repository.UpsertProgression(MakeFor("adeptII#4", 2, GameMode.GM_1v1, null, 2, 2, 99));
        await _repository.UpsertProgression(MakeFor("diamondI#5", 2, GameMode.GM_1v1, null, 4, 1, 99));
        await _repository.UpsertProgression(MakeFor("othermode#6", 2, GameMode.GM_2v2, null, 2, 1, 99));
        await _repository.UpsertProgression(MakeFor("otherseason#7", 1, GameMode.GM_1v1, null, 2, 1, 99));

        var result = await _repository.LoadPlayersByProgressionLeague(2, GameMode.GM_1v1, 2, 1, null, 0, 100);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(new[] { 90, 50, 10 }, result.Select(r => r.Points).ToArray());
        Assert.AreEqual("high#2", result[0].PlayerIds[0].BattleTag);
    }

    [Test]
    public async Task LoadPlayersByProgressionLeague_ApexLeagues_ReturnEmpty()
    {
        // Leagues 0 (Grand Master) and 1 (Master) are apex — served by /api/ladder/apex, not here.
        await _repository.UpsertProgression(MakeFor("gm#1", 2, GameMode.GM_1v1, null, 0, 1, 99));
        await _repository.UpsertProgression(MakeFor("master#2", 2, GameMode.GM_1v1, null, 1, 1, 99));

        var grandMaster = await _repository.LoadPlayersByProgressionLeague(2, GameMode.GM_1v1, 0, 1, null, 0, 100);
        var master = await _repository.LoadPlayersByProgressionLeague(2, GameMode.GM_1v1, 1, 1, null, 0, 100);

        Assert.IsEmpty(grandMaster);
        Assert.IsEmpty(master);
    }

    [Test]
    public async Task LoadPlayersByProgressionLeague_FiltersByRace_WhenRaceProvided()
    {
        // Race-split mode: same league/division, different races.
        await _repository.UpsertProgression(MakeFor("hu#1", 2, GameMode.GM_1v1, Race.HU, 2, 1, 80));
        await _repository.UpsertProgression(MakeFor("ne#2", 2, GameMode.GM_1v1, Race.NE, 2, 1, 70));

        var huOnly = await _repository.LoadPlayersByProgressionLeague(2, GameMode.GM_1v1, 2, 1, Race.HU, 0, 100);

        Assert.AreEqual(1, huOnly.Count);
        Assert.AreEqual("hu#1", huOnly[0].PlayerIds[0].BattleTag);
        Assert.AreEqual(Race.HU, huOnly[0].Race);
    }

    [Test]
    public async Task LoadPlayersByProgressionLeague_NullRace_IgnoresRaceFilter()
    {
        await _repository.UpsertProgression(MakeFor("hu#1", 2, GameMode.GM_1v1, Race.HU, 2, 1, 80));
        await _repository.UpsertProgression(MakeFor("ne#2", 2, GameMode.GM_1v1, Race.NE, 2, 1, 70));

        var all = await _repository.LoadPlayersByProgressionLeague(2, GameMode.GM_1v1, 2, 1, null, 0, 100);

        Assert.AreEqual(2, all.Count);
    }

    [Test]
    public async Task LoadPlayersByProgressionLeague_AppliesSkipAndTake()
    {
        for (int i = 0; i < 5; i++)
        {
            await _repository.UpsertProgression(MakeFor($"p{i}#1", 2, GameMode.GM_1v1, null, 2, 1, 100 - i * 10));
        }

        var page = await _repository.LoadPlayersByProgressionLeague(2, GameMode.GM_1v1, 2, 1, null, 1, 2);

        // Points desc: 100, 90, 80, 70, 60 -> skip 1, take 2 -> 90, 80
        Assert.AreEqual(2, page.Count);
        Assert.AreEqual(new[] { 90, 80 }, page.Select(r => r.Points).ToArray());
    }

    private static PlayerProgression MakeUnplaced(string battleTag, int season)
    {
        var id = new BattleTagIdCombined(
            new List<PlayerId> { PlayerId.Create(battleTag) },
            GateWay.Europe, GameMode.GM_1v1, season, Race.HU);
        return PlayerProgression.Create(id); // League stays null
    }

    [Test]
    public async Task CountByBracket_GroupsByBracket_ExcludesUnplacedAndOtherSeasons()
    {
        await _repository.UpsertProgression(Make("a#1", 2, 5, 1, 10));
        await _repository.UpsertProgression(Make("b#2", 2, 5, 1, 20));
        await _repository.UpsertProgression(Make("c#3", 2, 5, 2, 30));
        await _repository.UpsertProgression(MakeUnplaced("d#4", 2));
        await _repository.UpsertProgression(Make("e#5", 1, 5, 1, 40));

        var counts = await _repository.CountByBracket(2);

        Assert.AreEqual(2, counts.Single(c => c.League == 5 && c.Division == 1).Count);
        Assert.AreEqual(1, counts.Single(c => c.League == 5 && c.Division == 2).Count);
        Assert.AreEqual(2, counts.Count);
    }
}
