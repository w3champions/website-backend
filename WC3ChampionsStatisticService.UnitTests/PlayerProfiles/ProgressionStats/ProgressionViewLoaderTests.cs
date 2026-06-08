using System.Collections.Generic;
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
public class ProgressionViewLoaderTests
{
    private MongoDbRunner _runner;
    private MongoClient _mongoClient;
    private PlayerProgressionRepository _repository;
    private ProgressionViewLoader _loader;

    [SetUp]
    public void Setup()
    {
        _runner = MongoDbRunner.Start();
        _mongoClient = new MongoClient(_runner.ConnectionString);
        _repository = new PlayerProgressionRepository(_mongoClient);
        _loader = new ProgressionViewLoader(_repository);
    }

    [TearDown]
    public void TearDown() => _runner.Dispose();

    private static PlayerProgression Make(string battleTag, int league, int division, int points)
    {
        var id = new BattleTagIdCombined(
            new List<PlayerId> { PlayerId.Create(battleTag) },
            // season fixed (race-split era); loader tests don't differ by season
            GateWay.Europe, GameMode.GM_1v1, 2, Race.HU);
        var p = PlayerProgression.Create(id);
        p.RecordRank(league, division, points, null);
        return p;
    }

    [Test]
    public async Task LoadViews_MapsById_OmitsMissing()
    {
        var a = Make("a#1", 3, 2, 50);
        await _repository.UpsertProgression(a);

        var views = await _loader.LoadViews(new List<string> { a.Id, "missing#9" });

        Assert.IsTrue(views.ContainsKey(a.Id));
        Assert.AreEqual(3, views[a.Id].League);
        Assert.AreEqual(2, views[a.Id].Division);
        Assert.AreEqual(50, views[a.Id].Points);
        Assert.IsNull(views[a.Id].ApexPoints);
        Assert.IsFalse(views.ContainsKey("missing#9"));
    }

    [Test]
    public async Task LoadViews_EmptyInput_ReturnsEmptyMap()
    {
        var views = await _loader.LoadViews(new List<string>());
        Assert.IsEmpty(views);
    }
}
