using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mongo2Go;
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
}
