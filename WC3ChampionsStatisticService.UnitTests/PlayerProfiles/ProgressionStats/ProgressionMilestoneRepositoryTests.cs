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
public class ProgressionMilestoneRepositoryTests
{
    private MongoDbRunner _runner;
    private MongoClient _mongoClient;
    private ProgressionMilestoneRepository _repository;

    [SetUp]
    public void Setup()
    {
        _runner = MongoDbRunner.Start();
        _mongoClient = new MongoClient(_runner.ConnectionString);
        _repository = new ProgressionMilestoneRepository(_mongoClient);
    }

    [TearDown]
    public void TearDown() => _runner.Dispose();

    private static List<PlayerId> Tags(params string[] tags) => tags.Select(PlayerId.Create).ToList();

    [Test]
    public async Task UpsertAndLoad_RoundTrips()
    {
        var m = ProgressionMilestone.Create(Tags("zed#1"), GateWay.Europe, GameMode.GM_1v1, Race.HU);
        m.RecordWin();
        m.RecordActivity(new System.DateTimeOffset(2026, 6, 3, 0, 0, 0, System.TimeSpan.Zero));
        await _repository.UpsertMilestone(m);

        var loaded = await _repository.LoadMilestone("zed#1@20_GM_1v1_HU");
        Assert.IsNotNull(loaded);
        Assert.AreEqual(1, loaded.TotalWins);
        Assert.AreEqual(1, loaded.ActivityWeeks.Count);
        Assert.AreEqual(GameMode.GM_1v1, loaded.GameMode);
        Assert.AreEqual(Race.HU, loaded.Race);
    }

    [Test]
    public async Task Upsert_SameId_KeepsSingleDoc()
    {
        var m = ProgressionMilestone.Create(Tags("zed#1"), GateWay.Europe, GameMode.GM_1v1, Race.HU);
        m.RecordWin();
        await _repository.UpsertMilestone(m);
        m.RecordWin();
        await _repository.UpsertMilestone(m);

        var loaded = await _repository.LoadMilestone("zed#1@20_GM_1v1_HU");
        Assert.AreEqual(2, loaded.TotalWins);
        var count = await _mongoClient.GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<ProgressionMilestone>("ProgressionMilestone")
            .CountDocumentsAsync(FilterDefinition<ProgressionMilestone>.Empty);
        Assert.AreEqual(1, count);
    }

    [Test]
    public async Task LoadMilestone_MissingId_ReturnsNull()
    {
        var loaded = await _repository.LoadMilestone("nobody#0@20_GM_1v1_HU");
        Assert.IsNull(loaded);
    }

    [Test]
    public async Task LoadMilestonesForPlayer_ReturnsSoloAndAtMember_ExcludesUnrelated()
    {
        // A solo (1v1) doc for the player.
        var solo = ProgressionMilestone.Create(Tags("zed#1"), GateWay.Europe, GameMode.GM_1v1, Race.HU);
        solo.RecordWin();
        await _repository.UpsertMilestone(solo);

        // An arranged-team doc whose PlayerIds includes the player (alongside a teammate).
        var atTeam = ProgressionMilestone.Create(Tags("zed#1", "ally#7"), GateWay.Europe, GameMode.GM_2v2_AT, null);
        atTeam.RecordWin();
        await _repository.UpsertMilestone(atTeam);

        // An unrelated player's doc that must NOT be returned.
        var unrelated = ProgressionMilestone.Create(Tags("ka#2"), GateWay.Europe, GameMode.GM_1v1, Race.NE);
        unrelated.RecordWin();
        await _repository.UpsertMilestone(unrelated);

        var loaded = await _repository.LoadMilestonesForPlayer("zed#1");

        var ids = loaded.Select(m => m.Id).OrderBy(id => id).ToList();
        Assert.AreEqual(2, loaded.Count);
        CollectionAssert.AreEquivalent(new[] { solo.Id, atTeam.Id }, ids);
    }

    [Test]
    public async Task EnsureIndexes_creates_player_battletag_multikey()
    {
        await _repository.EnsureIndexesAsync();

        var coll = _mongoClient
            .GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<ProgressionMilestone>(typeof(ProgressionMilestone).Name);
        var indexes = await (await coll.Indexes.ListAsync()).ToListAsync();
        var indexDump = $"count={indexes.Count}; " + string.Join(" | ", indexes.Select(i => i.ToJson()));

        // The owner read (LoadMilestonesForPlayer) filters on PlayerIds.BattleTag; the
        // multikey index keys on exactly that nested-array path.
        Assert.That(
            indexes.Any(i => i.GetValue("name", BsonString.Empty).AsString == "PlayerIds.BattleTag_1"),
            Is.True,
            "PlayerIds.BattleTag multikey index missing — got: " + indexDump);
    }

    [Test]
    public async Task EnsureIndexes_is_idempotent()
    {
        await _repository.EnsureIndexesAsync();
        // Identical key + options must be a no-op on the second call.
        Assert.DoesNotThrowAsync(async () => await _repository.EnsureIndexesAsync());
    }
}
