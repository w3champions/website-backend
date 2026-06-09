using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Mongo2Go;
using MongoDB.Driver;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles;

[TestFixture]
public class PlayersControllerMyMilestonesTests
{
    private MongoDbRunner _runner;
    private MongoClient _mongoClient;
    private ProgressionMilestoneRepository _milestoneRepository;
    private PlayersController _controller;

    [SetUp]
    public void Setup()
    {
        _runner = MongoDbRunner.Start();
        _mongoClient = new MongoClient(_runner.ConnectionString);
        _milestoneRepository = new ProgressionMilestoneRepository(_mongoClient);
        var milestoneQueryHandler = new MilestoneQueryHandler(_milestoneRepository);

        // Only the milestone path is exercised here; the other dependencies are not touched by
        // GetMyMilestones, so they are left null (mirrors the focused controller-test idiom).
        _controller = new PlayersController(
            playerRepository: null,
            queryHandler: null,
            milestoneQueryHandler: milestoneQueryHandler,
            personalSettingsRepository: null,
            clanRepository: null,
            playerAkaProvider: null,
            playerService: null,
            battleTagResolver: null);
    }

    [TearDown]
    public void TearDown() => _runner.Dispose();

    private static List<PlayerId> Tags(params string[] tags) => tags.Select(PlayerId.Create).ToList();

    private async Task SeedMilestone(List<PlayerId> playerIds, GameMode gameMode, Race? race, int wins)
    {
        var m = ProgressionMilestone.Create(playerIds, GateWay.Europe, gameMode, race);
        for (var i = 0; i < wins; i++)
        {
            m.RecordWin();
        }
        await _milestoneRepository.UpsertMilestone(m);
    }

    [Test]
    public async Task GetMyMilestones_ReturnsCallersSoloAndAtMilestones_MappedToDto()
    {
        // Caller's solo 1v1 milestone.
        await SeedMilestone(Tags("zed#1"), GameMode.GM_1v1, Race.HU, 53);
        // An arranged-team milestone the caller belongs to (with a teammate).
        await SeedMilestone(Tags("zed#1", "ally#7"), GameMode.GM_2v2_AT, null, 12);
        // A second player's milestone that must NOT leak to this caller.
        await SeedMilestone(Tags("other#9"), GameMode.GM_1v1, Race.NE, 99);

        var result = await _controller.GetMyMilestones("zed#1");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var dtos = ((OkObjectResult)result).Value as List<MilestoneDto>;
        Assert.IsNotNull(dtos);
        Assert.AreEqual(2, dtos.Count);

        var solo = dtos.Single(d => d.GameMode == GameMode.GM_1v1);
        Assert.AreEqual(Race.HU, solo.Race);
        Assert.AreEqual(53, solo.CurrentWins);
        Assert.AreEqual(50, solo.PreviousTarget);
        Assert.Greater(solo.NextTarget, 53);

        var at = dtos.Single(d => d.GameMode == GameMode.GM_2v2_AT);
        Assert.IsNull(at.Race);
        Assert.AreEqual(12, at.CurrentWins);

        // The other player's milestone is absent.
        Assert.IsFalse(dtos.Any(d => d.CurrentWins == 99));
    }

    [Test]
    public async Task GetMyMilestones_ReturnsEmptyList_WhenCallerHasNoMilestones()
    {
        await SeedMilestone(Tags("other#9"), GameMode.GM_1v1, Race.NE, 99);

        var result = await _controller.GetMyMilestones("zed#1");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var dtos = ((OkObjectResult)result).Value as List<MilestoneDto>;
        Assert.IsNotNull(dtos);
        Assert.IsEmpty(dtos);
    }
}
