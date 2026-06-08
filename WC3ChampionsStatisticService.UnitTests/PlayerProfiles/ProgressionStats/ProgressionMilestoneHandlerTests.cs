using System;
using System.Collections.Generic;
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
public class ProgressionMilestoneHandlerTests
{
    private MongoDbRunner _runner;
    private MongoClient _mongoClient;
    private ProgressionMilestoneRepository _repository;
    private ProgressionMilestoneHandler _handler;

    [SetUp]
    public void Setup()
    {
        _runner = MongoDbRunner.Start();
        _mongoClient = new MongoClient(_runner.ConnectionString);
        _repository = new ProgressionMilestoneRepository(_mongoClient);
        _handler = new ProgressionMilestoneHandler(_repository);
    }

    [TearDown]
    public void TearDown() => _runner.Dispose();

    private static long Ms(int year, int month, int day) =>
        new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private static PlayerMMrChange Player(string battleTag, int team, bool won, Race race, string atTeamId = null)
    {
        return new PlayerMMrChange
        {
            battleTag = battleTag,
            team = team,
            won = won,
            race = race,
            atTeamId = atTeamId,
        };
    }

    private static MatchFinishedEvent Event(GameMode gameMode, int season, GateWay gateway, long endTimeMs,
        List<PlayerMMrChange> players, bool wasFake = false)
    {
        return new MatchFinishedEvent
        {
            WasFakeEvent = wasFake,
            match = new Match
            {
                id = "m-" + season + "-" + endTimeMs,
                gameMode = gameMode,
                gateway = gateway,
                season = season,
                endTime = endTimeMs,
                players = players,
            },
        };
    }

    private Task<ProgressionMilestone> Load(string id) => _repository.LoadMilestone(id);
    private Task<long> Count() => _mongoClient.GetDatabase("W3Champions-Statistic-Service")
        .GetCollection<ProgressionMilestone>(nameof(ProgressionMilestone))
        .CountDocumentsAsync(FilterDefinition<ProgressionMilestone>.Empty);

    [Test]
    public async Task Win_IncrementsTotalWins_AndRecordsActivity()
    {
        await _handler.Update(Event(GameMode.GM_1v1, 2, GateWay.Europe, Ms(2026, 6, 3), new List<PlayerMMrChange>
        {
            Player("winner#1", 0, true, Race.HU),
            Player("loser#2", 1, false, Race.OC),
        }));

        var winner = await Load("winner#1@20_GM_1v1_HU");
        var loser = await Load("loser#2@20_GM_1v1_OC");
        Assert.AreEqual(1, winner.TotalWins);
        Assert.AreEqual(1, winner.ActivityWeeks.Count);
        Assert.AreEqual(0, loser.TotalWins);              // loss does not increment wins…
        Assert.AreEqual(1, loser.ActivityWeeks.Count);    // …but DOES record activity
    }

    [Test]
    public async Task AtTeam_SharesOneDoc_NormalizedGameMode_NoRace()
    {
        await _handler.Update(Event(GameMode.GM_2v2, 2, GateWay.Europe, Ms(2026, 6, 3), new List<PlayerMMrChange>
        {
            Player("a#1", 0, true, Race.HU, atTeamId: "T1"),
            Player("b#2", 0, true, Race.OC, atTeamId: "T1"),
            Player("c#3", 1, false, Race.NE, atTeamId: "T2"),
            Player("d#4", 1, false, Race.UD, atTeamId: "T2"),
        }));

        Assert.AreEqual(2, await Count()); // one doc per AT team, not per player
        var t1 = await Load("a#1@20_b#2@20_GM_2v2_AT");
        Assert.IsNotNull(t1);
        Assert.AreEqual(1, t1.TotalWins);
        Assert.IsNull(t1.Race);

        var t2 = await Load("c#3@20_d#4@20_GM_2v2_AT");
        Assert.IsNotNull(t2);
        Assert.AreEqual(0, t2.TotalWins);
        Assert.AreEqual(1, t2.ActivityWeeks.Count); // loss still records activity
    }

    [Test]
    public async Task TwoAtTeams_SameGameTeamNumber_WriteSeparateDocs()
    {
        await _handler.Update(Event(GameMode.GM_2v2, 2, GateWay.Europe, Ms(2026, 6, 3), new List<PlayerMMrChange>
        {
            Player("a#1", 0, true, Race.HU, atTeamId: "T1"),
            Player("b#2", 0, true, Race.OC, atTeamId: "T1"),
            Player("e#5", 0, true, Race.HU, atTeamId: "T3"), // same game-team 0, different AT team
            Player("f#6", 0, true, Race.OC, atTeamId: "T3"),
            Player("c#3", 1, false, Race.NE, atTeamId: "T2"),
            Player("d#4", 1, false, Race.UD, atTeamId: "T2"),
        }));

        Assert.IsNotNull(await Load("a#1@20_b#2@20_GM_2v2_AT"));
        Assert.IsNotNull(await Load("e#5@20_f#6@20_GM_2v2_AT"));
        Assert.AreEqual(3, await Count());
    }

    [Test]
    public async Task Solo1v1_KeyedPerRace()
    {
        await _handler.Update(Event(GameMode.GM_1v1, 2, GateWay.Europe, Ms(2026, 6, 3), new List<PlayerMMrChange>
        {
            Player("zed#1", 0, true, Race.HU),
        }));
        await _handler.Update(Event(GameMode.GM_1v1, 2, GateWay.Europe, Ms(2026, 6, 4), new List<PlayerMMrChange>
        {
            Player("zed#1", 0, true, Race.OC),
        }));

        Assert.AreEqual(1, (await Load("zed#1@20_GM_1v1_HU")).TotalWins);
        Assert.AreEqual(1, (await Load("zed#1@20_GM_1v1_OC")).TotalWins);
    }

    [Test]
    public async Task CrossSeason_AccumulatesIntoSameDoc()
    {
        await _handler.Update(Event(GameMode.GM_1v1, 1, GateWay.Europe, Ms(2025, 6, 3), new List<PlayerMMrChange>
        {
            Player("zed#1", 0, true, Race.HU),
        }));
        await _handler.Update(Event(GameMode.GM_1v1, 2, GateWay.Europe, Ms(2026, 6, 3), new List<PlayerMMrChange>
        {
            Player("zed#1", 0, true, Race.HU),
        }));

        Assert.AreEqual(1, await Count());                       // season-less key → one doc
        Assert.AreEqual(2, (await Load("zed#1@20_GM_1v1_HU")).TotalWins);
    }

    [Test]
    public async Task FakeEvent_IsSkipped()
    {
        await _handler.Update(Event(GameMode.GM_1v1, 2, GateWay.Europe, Ms(2026, 6, 3), new List<PlayerMMrChange>
        {
            Player("zed#1", 0, true, Race.HU),
        }, wasFake: true));

        Assert.AreEqual(0, await Count());
    }

    [Test]
    public async Task Replay_AtLeastOnce_RecountsUnderCursorlessReplay()
    {
        // Documents the chosen cursor-only at-least-once behaviour: replaying the same event
        // re-applies (handler has no per-doc guard; the read-model cursor prevents this in prod).
        var ev = Event(GameMode.GM_1v1, 2, GateWay.Europe, Ms(2026, 6, 3), new List<PlayerMMrChange>
        {
            Player("zed#1", 0, true, Race.HU),
        });
        await _handler.Update(ev);
        await _handler.Update(ev);

        Assert.AreEqual(2, (await Load("zed#1@20_GM_1v1_HU")).TotalWins);
    }

    [Test]
    public async Task MixedAtAndSolo_InSameMatch_WriteCorrectDocs()
    {
        await _handler.Update(Event(GameMode.GM_2v2, 2, GateWay.Europe, Ms(2026, 6, 3), new List<PlayerMMrChange>
        {
            Player("a#1", 0, true, Race.HU, atTeamId: "T1"),   // AT pair, won
            Player("b#2", 0, true, Race.OC, atTeamId: "T1"),
            Player("c#3", 1, false, Race.NE),                  // solo-queued, lost (IsAt false)
            Player("d#4", 1, false, Race.UD),                  // solo-queued, lost
        }));

        Assert.AreEqual(3, await Count()); // 1 AT-pair doc + 2 solo docs
        var atPair = await Load("a#1@20_b#2@20_GM_2v2_AT");
        Assert.AreEqual(1, atPair.TotalWins);
        Assert.IsNull(atPair.Race);
        var solo1 = await Load("c#3@20_GM_2v2");   // solos key under the base (non-AT) variant, no race
        var solo2 = await Load("d#4@20_GM_2v2");
        Assert.AreEqual(0, solo1.TotalWins);
        Assert.AreEqual(0, solo2.TotalWins);
    }

    [Test]
    public async Task EmptyPlayers_WritesNothing()
    {
        await _handler.Update(Event(GameMode.GM_1v1, 2, GateWay.Europe, Ms(2026, 6, 3), new List<PlayerMMrChange>()));
        Assert.AreEqual(0, await Count());
    }
}
