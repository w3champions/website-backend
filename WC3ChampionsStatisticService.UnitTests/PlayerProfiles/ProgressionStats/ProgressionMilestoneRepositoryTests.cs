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
    public async Task LoadMilestones_ReturnsOnlyMatchingDocs()
    {
        var a = ProgressionMilestone.Create(Tags("zed#1"), GateWay.Europe, GameMode.GM_1v1, Race.HU);
        a.RecordWin();
        await _repository.UpsertMilestone(a);

        var b = ProgressionMilestone.Create(Tags("ka#2"), GateWay.Europe, GameMode.GM_1v1, Race.NE);
        b.RecordWin();
        await _repository.UpsertMilestone(b);

        var loaded = await _repository.LoadMilestones(new List<string> { a.Id, "missing#9@20_GM_1v1_HU" });

        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual(a.Id, loaded[0].Id);
    }
}
