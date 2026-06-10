using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ladder;

namespace WC3ChampionsStatisticService.Tests.Ladder;

[TestFixture]
public class ApexLeaderboardTests : IntegrationTestBase
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task InsertApexStandingsEvent(ApexStandingsChangedEvent ev)
    {
        var db = MongoClient.GetDatabase("W3Champions-Statistic-Service");
        var col = db.GetCollection<ApexStandingsChangedEvent>(nameof(ApexStandingsChangedEvent));
        await col.FindOneAndReplaceAsync<ApexStandingsChangedEvent>(
            e => e.id == ev.id,
            ev,
            new FindOneAndReplaceOptions<ApexStandingsChangedEvent> { IsUpsert = true });
    }

    private static ApexStandingsChangedEvent MakeEvent(
        int season,
        GameMode gameMode,
        bool wasSynced = false,
        int? cutoff = 800,
        int gmCount = 2)
    {
        int id = season * 100000 + (int)gameMode;
        return new ApexStandingsChangedEvent
        {
            id = id,
            season = season,
            gameMode = gameMode,
            cutoffApexPoints = cutoff,
            gmCount = gmCount,
            wasSyncedJustNow = wasSynced,
            players = new[]
            {
                new ApexStandingRaw { battleTags = new List<string> { "alpha#1" },         race = Race.HU, apexPoints = 1000, league = 0 },
                new ApexStandingRaw { battleTags = new List<string> { "beta#2" },          race = Race.NE, apexPoints = 900,  league = 0 },
                new ApexStandingRaw { battleTags = new List<string> { "gamma#3", "delta#4" }, race = null, apexPoints = 850, league = 1 },
                new ApexStandingRaw { battleTags = new List<string> { "epsilon#5" },        race = Race.UD, apexPoints = 820, league = 1 },
                new ApexStandingRaw { battleTags = new List<string> { "zeta#6" },           race = Race.OC, apexPoints = 810, league = 1 },
            },
        };
    }

    // ── Checkout tests ────────────────────────────────────────────────────────

    [Test]
    public async Task Checkout_ReturnsOnlyUnsyncedEvents_AndMarksThem()
    {
        // Arrange — one unsynced, one already-synced
        var unsynced = MakeEvent(season: 22, gameMode: GameMode.GM_1v1, wasSynced: false);
        var synced = MakeEvent(season: 22, gameMode: GameMode.GM_2v2, wasSynced: true);

        await InsertApexStandingsEvent(unsynced);
        await InsertApexStandingsEvent(synced);

        var repo = new MatchEventRepository(MongoClient);

        // Act
        var result = await repo.CheckoutApexStandingsChanged();

        // Assert: only the unsynced one returned
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(unsynced.id, result[0].id);

        // Calling again should return nothing (was marked synced)
        var second = await repo.CheckoutApexStandingsChanged();
        Assert.AreEqual(0, second.Count);
    }

    [Test]
    public async Task Checkout_ReturnsNothing_WhenAllAlreadySynced()
    {
        var synced = MakeEvent(season: 5, gameMode: GameMode.GM_1v1, wasSynced: true);
        await InsertApexStandingsEvent(synced);

        var repo = new MatchEventRepository(MongoClient);
        var result = await repo.CheckoutApexStandingsChanged();

        Assert.AreEqual(0, result.Count);
    }

    // ── Repository round-trip tests ───────────────────────────────────────────

    [Test]
    public async Task Repo_UpsertAndLoad_RoundTrip()
    {
        var repo = new ApexLeaderboardRepository(MongoClient);

        var leaderboard = new ApexLeaderboard
        {
            Id = "22_1",
            Season = 22,
            GameMode = GameMode.GM_1v1,
            CutoffApexPoints = 750,
            GmCount = 3,
            Players = new List<ApexLeaderboardEntry>
            {
                new() { BattleTags = new List<string> { "player1#1" }, Race = Race.HU, ApexPoints = 1200, League = 0, RankNumber = 1 },
                new() { BattleTags = new List<string> { "player2#2" }, Race = Race.NE, ApexPoints = 1100, League = 0, RankNumber = 2 },
                new() { BattleTags = new List<string> { "player3#3", "partner#4" }, Race = null, ApexPoints = 900, League = 1, RankNumber = 3 },
            },
        };

        await repo.UpsertOne(leaderboard);

        var loaded = await repo.LoadApexLeaderboard(season: 22, GameMode.GM_1v1);

        Assert.IsNotNull(loaded);
        Assert.AreEqual("22_1", loaded.Id);
        Assert.AreEqual(22, loaded.Season);
        Assert.AreEqual(GameMode.GM_1v1, loaded.GameMode);
        Assert.AreEqual(750, loaded.CutoffApexPoints);
        Assert.AreEqual(3, loaded.GmCount);

        Assert.AreEqual(3, loaded.Players.Count);
        Assert.AreEqual("player1#1", loaded.Players[0].BattleTags[0]);
        Assert.AreEqual(Race.HU, loaded.Players[0].Race);
        Assert.AreEqual(1200, loaded.Players[0].ApexPoints);
        Assert.AreEqual(0, loaded.Players[0].League);
        Assert.AreEqual(1, loaded.Players[0].RankNumber);

        // AT entry with two tags preserved
        Assert.AreEqual(2, loaded.Players[2].BattleTags.Count);
        Assert.IsNull(loaded.Players[2].Race);
        Assert.AreEqual(3, loaded.Players[2].RankNumber);
    }

    [Test]
    public async Task Repo_Load_ReturnsNull_WhenNotFound()
    {
        var repo = new ApexLeaderboardRepository(MongoClient);
        var result = await repo.LoadApexLeaderboard(season: 99, GameMode.GM_1v1);
        Assert.IsNull(result);
    }

    // ── Handler integration tests ─────────────────────────────────────────────

    [Test]
    public async Task Handler_BuildsLeaderboard_WithCorrectRankNumbers_AndCutoff()
    {
        // Arrange
        var ev = MakeEvent(season: 22, gameMode: GameMode.GM_1v1, wasSynced: false, cutoff: 800, gmCount: 2);
        await InsertApexStandingsEvent(ev);

        var matchEventRepo = new MatchEventRepository(MongoClient);
        var leaderboardRepo = new ApexLeaderboardRepository(MongoClient);
        var handler = new ApexSyncHandler(leaderboardRepo, matchEventRepo);

        // Act
        await handler.Update();

        // Assert
        var doc = await leaderboardRepo.LoadApexLeaderboard(season: 22, GameMode.GM_1v1);

        Assert.IsNotNull(doc);
        Assert.AreEqual(800, doc.CutoffApexPoints);
        Assert.AreEqual(2, doc.GmCount);
        Assert.AreEqual(5, doc.Players.Count);

        // GM entries first (league=0), then Master (league=1), order preserved from event
        Assert.AreEqual(0, doc.Players[0].League);
        Assert.AreEqual(1000, doc.Players[0].ApexPoints);
        Assert.AreEqual(1, doc.Players[0].RankNumber);

        Assert.AreEqual(0, doc.Players[1].League);
        Assert.AreEqual(900, doc.Players[1].ApexPoints);
        Assert.AreEqual(2, doc.Players[1].RankNumber);

        Assert.AreEqual(1, doc.Players[2].League);
        Assert.AreEqual(850, doc.Players[2].ApexPoints);
        Assert.AreEqual(3, doc.Players[2].RankNumber);
        Assert.AreEqual(2, doc.Players[2].BattleTags.Count);  // AT entry preserved

        Assert.AreEqual(5, doc.Players[4].RankNumber);
    }

    [Test]
    public async Task Handler_ReplacesDocument_WhenNewerEventArrives()
    {
        // Seed first event
        var first = MakeEvent(season: 22, gameMode: GameMode.GM_1v1, wasSynced: false, cutoff: 800, gmCount: 2);
        await InsertApexStandingsEvent(first);

        var matchEventRepo = new MatchEventRepository(MongoClient);
        var leaderboardRepo = new ApexLeaderboardRepository(MongoClient);
        var handler = new ApexSyncHandler(leaderboardRepo, matchEventRepo);

        await handler.Update();

        // Verify first event written
        var afterFirst = await leaderboardRepo.LoadApexLeaderboard(22, GameMode.GM_1v1);
        Assert.AreEqual(5, afterFirst.Players.Count);
        Assert.AreEqual(800, afterFirst.CutoffApexPoints);

        // Seed a newer event for the SAME season+gameMode (cutoff has changed, fewer players)
        var second = new ApexStandingsChangedEvent
        {
            id = 22 * 100000 + (int)GameMode.GM_1v1,
            season = 22,
            gameMode = GameMode.GM_1v1,
            cutoffApexPoints = 950,
            gmCount = 1,
            wasSyncedJustNow = false,
            players = new[]
            {
                new ApexStandingRaw { battleTags = new List<string> { "top#1" }, race = Race.HU, apexPoints = 1500, league = 0 },
                new ApexStandingRaw { battleTags = new List<string> { "second#2" }, race = Race.NE, apexPoints = 1200, league = 1 },
            },
        };
        await InsertApexStandingsEvent(second);

        await handler.Update();

        // Assert: document replaced (not appended)
        var afterSecond = await leaderboardRepo.LoadApexLeaderboard(22, GameMode.GM_1v1);
        Assert.AreEqual(2, afterSecond.Players.Count);
        Assert.AreEqual(950, afterSecond.CutoffApexPoints);
        Assert.AreEqual(1, afterSecond.GmCount);
        Assert.AreEqual("top#1", afterSecond.Players[0].BattleTags[0]);
        Assert.AreEqual(1, afterSecond.Players[0].RankNumber);
        Assert.AreEqual(2, afterSecond.Players[1].RankNumber);
    }

    [Test]
    public async Task Handler_DoesNothing_WhenNoEvents()
    {
        var matchEventRepo = new MatchEventRepository(MongoClient);
        var leaderboardRepo = new ApexLeaderboardRepository(MongoClient);
        var handler = new ApexSyncHandler(leaderboardRepo, matchEventRepo);

        // Should not throw
        await handler.Update();

        var doc = await leaderboardRepo.LoadApexLeaderboard(22, GameMode.GM_1v1);
        Assert.IsNull(doc);
    }

    [Test]
    public async Task Handler_BuildsEmptyLeaderboard_WhenCohortIsNull_AndCutoffIsNull()
    {
        // Realistic season-start "no GM yet" state: no players, no cutoff.
        var ev = new ApexStandingsChangedEvent
        {
            id = 22 * 100000 + (int)GameMode.GM_1v1,
            season = 22,
            gameMode = GameMode.GM_1v1,
            cutoffApexPoints = null,
            gmCount = 0,
            wasSyncedJustNow = false,
            players = null,
        };
        await InsertApexStandingsEvent(ev);

        var matchEventRepo = new MatchEventRepository(MongoClient);
        var leaderboardRepo = new ApexLeaderboardRepository(MongoClient);
        var handler = new ApexSyncHandler(leaderboardRepo, matchEventRepo);

        // Should not throw
        await handler.Update();

        var doc = await leaderboardRepo.LoadApexLeaderboard(22, GameMode.GM_1v1);

        Assert.IsNotNull(doc);
        Assert.IsNull(doc.CutoffApexPoints);
        Assert.AreEqual(0, doc.GmCount);
        Assert.IsNotNull(doc.Players);
        Assert.IsEmpty(doc.Players);
    }

    [Test]
    public async Task Handler_DefaultsBattleTagsToEmptyList_WhenRawBattleTagsAreNull()
    {
        // A standing whose battleTags field is absent in Mongo deserializes to null;
        // the handler must default it to an empty list so downstream consumers don't crash.
        var ev = new ApexStandingsChangedEvent
        {
            id = 22 * 100000 + (int)GameMode.GM_1v1,
            season = 22,
            gameMode = GameMode.GM_1v1,
            cutoffApexPoints = 800,
            gmCount = 1,
            wasSyncedJustNow = false,
            players = new[]
            {
                new ApexStandingRaw { battleTags = null, race = Race.HU, apexPoints = 1000, league = 0 },
            },
        };
        await InsertApexStandingsEvent(ev);

        var matchEventRepo = new MatchEventRepository(MongoClient);
        var leaderboardRepo = new ApexLeaderboardRepository(MongoClient);
        var handler = new ApexSyncHandler(leaderboardRepo, matchEventRepo);

        await handler.Update();

        var doc = await leaderboardRepo.LoadApexLeaderboard(22, GameMode.GM_1v1);

        Assert.IsNotNull(doc);
        Assert.AreEqual(1, doc.Players.Count);
        Assert.IsNotNull(doc.Players[0].BattleTags);
        Assert.IsEmpty(doc.Players[0].BattleTags);
        Assert.AreEqual(1, doc.Players[0].RankNumber);
    }
}
