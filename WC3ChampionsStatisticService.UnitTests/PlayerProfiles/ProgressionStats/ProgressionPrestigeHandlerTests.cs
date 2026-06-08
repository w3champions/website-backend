using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Driver;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles.ProgressionStats;

[TestFixture]
public class ProgressionPrestigeHandlerTests
{
    private MongoDbRunner _runner;
    private MongoClient _mongoClient;
    private ProgressionPrestigeRepository _repo;
    private ProgressionPrestigeHandler _handler;

    [SetUp]
    public void Setup()
    {
        _runner = MongoDbRunner.Start();
        _mongoClient = new MongoClient(_runner.ConnectionString);
        _repo = new ProgressionPrestigeRepository(_mongoClient);
        _handler = new ProgressionPrestigeHandler(_repo);
    }

    [TearDown]
    public void TearDown() => _runner.Dispose();

    // --- helpers: copied from ProgressionMilestoneHandlerTests / PlayerProgressionHandlerTests ---

    private static PlayerMMrChange Solo(string battleTag, Race race, bool won, int? league, int? div, int? pts, int? apex = null)
    {
        return new PlayerMMrChange
        {
            battleTag = battleTag,
            team = 0,
            won = won,
            race = race,
            atTeamId = null,
            updatedProgression = league.HasValue
                ? new UpdatedProgression { league = league.Value, division = div ?? 0, points = pts ?? 0, apexPoints = apex }
                : null,
        };
    }

    private static PlayerMMrChange At(string battleTag, string atTeamId, int? league, int? div, int? pts)
    {
        return new PlayerMMrChange
        {
            battleTag = battleTag,
            team = 0,
            won = true,
            race = Race.HU,
            atTeamId = atTeamId,
            updatedProgression = league.HasValue
                ? new UpdatedProgression { league = league.Value, division = div ?? 0, points = pts ?? 0 }
                : null,
        };
    }

    private static MatchFinishedEvent Match(int season, GameMode mode, bool fake, params PlayerMMrChange[] players)
    {
        return new MatchFinishedEvent
        {
            WasFakeEvent = fake,
            match = new W3C.Domain.MatchmakingService.Match
            {
                id = "m-" + season,
                gameMode = mode,
                gateway = GateWay.Europe,
                season = season,
                endTime = 1_700_000_000_000L,
                players = new List<PlayerMMrChange>(players),
            },
        };
    }

    [Test]
    public async Task SoloPlacement_RecordsPeak()
    {
        await _handler.Update(Match(1, GameMode.GM_1v1, false,
            Solo("hero#1", Race.HU, won: true, league: 3, div: 1, pts: 50)));

        var loaded = await _repo.LoadPrestige("hero#1");
        Assert.IsNotNull(loaded);
        Assert.AreEqual(3, loaded.Peaks.Single().AllTimePeak.League);
        Assert.AreEqual(Race.HU, loaded.Peaks.Single().Race); // race in key for 1v1
    }

    [Test]
    public async Task ArrangedTeamPlayer_IsSkipped()
    {
        await _handler.Update(Match(1, GameMode.GM_2v2, false,
            At("teamguy#1", "teamA", league: 2, div: 1, pts: 50)));

        Assert.IsNull(await _repo.LoadPrestige("teamguy#1"));
    }

    [Test]
    public async Task NullProgression_IsSkipped()
    {
        await _handler.Update(Match(1, GameMode.GM_1v1, false,
            Solo("calib#1", Race.HU, won: false, league: null, div: null, pts: null)));

        Assert.IsNull(await _repo.LoadPrestige("calib#1"));
    }

    [Test]
    public async Task FakeEvent_IsSkipped()
    {
        await _handler.Update(Match(1, GameMode.GM_1v1, fake: true,
            Solo("fake#1", Race.HU, won: true, league: 3, div: 1, pts: 50)));

        Assert.IsNull(await _repo.LoadPrestige("fake#1"));
    }

    [Test]
    public async Task Replay_IsIdempotent()
    {
        var ev = Match(1, GameMode.GM_1v1, false, Solo("idem#1", Race.HU, true, 3, 1, 50));
        await _handler.Update(ev);
        await _handler.Update(ev);

        var loaded = await _repo.LoadPrestige("idem#1");
        Assert.AreEqual(1, loaded.Peaks.Single().SeasonPeaks.Count);
        Assert.AreEqual(3, loaded.Peaks.Single().AllTimePeak.League);
    }

    [Test]
    public async Task LaterDemotion_DoesNotLowerAllTimePeak()
    {
        await _handler.Update(Match(1, GameMode.GM_1v1, false, Solo("climb#1", Race.HU, true, 3, 1, 50)));
        await _handler.Update(Match(1, GameMode.GM_1v1, false, Solo("climb#1", Race.HU, false, 5, 4, 10)));

        var loaded = await _repo.LoadPrestige("climb#1");
        Assert.AreEqual(3, loaded.Peaks.Single().AllTimePeak.League);
    }

    [Test]
    public async Task CrossSeason_RetainsBothSeasonPeaks_AllTimeSurvives()
    {
        await _handler.Update(Match(1, GameMode.GM_1v1, false, Solo("vet#1", Race.HU, true, 3, 1, 50)));
        await _handler.Update(Match(2, GameMode.GM_1v1, false, Solo("vet#1", Race.HU, true, 6, 3, 20)));

        var entry = (await _repo.LoadPrestige("vet#1")).Peaks.Single();
        Assert.AreEqual(2, entry.SeasonPeaks.Count);
        Assert.AreEqual(3, entry.AllTimePeak.League);
    }
}
