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
public class MilestoneViewLoaderTests
{
    private MongoDbRunner _runner;
    private MongoClient _mongoClient;
    private ProgressionMilestoneRepository _repository;
    private MilestoneViewLoader _loader;

    [SetUp]
    public void Setup()
    {
        _runner = MongoDbRunner.Start();
        _mongoClient = new MongoClient(_runner.ConnectionString);
        _repository = new ProgressionMilestoneRepository(_mongoClient);
        _loader = new MilestoneViewLoader(_repository);
    }

    [TearDown]
    public void TearDown() => _runner.Dispose();

    private static ProgressionMilestone Make(string battleTag, Race race, int wins)
    {
        var m = ProgressionMilestone.Create(
            new List<PlayerId> { PlayerId.Create(battleTag) },
            GateWay.Europe, GameMode.GM_1v1, race);
        for (var i = 0; i < wins; i++)
        {
            m.RecordWin();
        }
        return m;
    }

    [Test]
    public async Task LoadViews_MapsById_OmitsMissing()
    {
        var a = Make("a#1", Race.HU, 53);
        var b = Make("b#2", Race.NE, 7);
        await _repository.UpsertMilestone(a);
        await _repository.UpsertMilestone(b);

        var views = await _loader.LoadViews(new List<string> { a.Id, b.Id, "missing#9@20_GM_1v1_HU" });

        Assert.IsTrue(views.ContainsKey(a.Id));
        Assert.AreEqual(53, views[a.Id].CurrentWins);
        Assert.AreEqual(50, views[a.Id].PreviousTarget);
        Assert.Greater(views[a.Id].NextTarget, 53);

        Assert.IsTrue(views.ContainsKey(b.Id));
        Assert.AreEqual(7, views[b.Id].CurrentWins);
        Assert.AreEqual(5, views[b.Id].PreviousTarget);
        Assert.Greater(views[b.Id].NextTarget, 7);

        Assert.IsFalse(views.ContainsKey("missing#9@20_GM_1v1_HU"));
    }

    [Test]
    public async Task LoadViews_EmptyInput_ReturnsEmptyMap()
    {
        var views = await _loader.LoadViews(new List<string>());
        Assert.IsEmpty(views);
    }
}
