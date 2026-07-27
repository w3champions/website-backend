using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Admin.Jobs;
using W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;

namespace WC3ChampionsStatisticService.Tests.Admin.Jobs;

[TestFixture]
public class PlayerMmrRpTimelineBackfillJobTests : IntegrationTestBase
{
    private const string Player = "peon#1";
    private static readonly DateTimeOffset Yesterday =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-1);

    private PlayerMmrRpTimelineBackfillJob _job;
    private FakeAdminJobContext _context;

    [SetUp]
    public void SetupJob()
    {
        _job = new PlayerMmrRpTimelineBackfillJob(MongoClient);
        _context = new FakeAdminJobContext();
    }

    private IMongoCollection<PlayerMmrRpTimeline> Timelines =>
        MongoClient.GetDatabase("W3Champions-Statistic-Service").GetCollection<PlayerMmrRpTimeline>(nameof(PlayerMmrRpTimeline));

    /// <param name="rd">Post-match rating deviation - what the live handler stores.</param>
    private async Task GivenMatch(
        DateTimeOffset endTime,
        int mmr,
        double rd = 100,
        Race race = Race.HU,
        string battleTag = Player,
        string atTeamId = null)
    {
        var finished = new MatchFinishedEvent
        {
            Id = ObjectId.GenerateNewId(endTime.UtcDateTime),
            match = new Match
            {
                id = Guid.NewGuid().ToString(),
                endTime = endTime.ToUnixTimeMilliseconds(),
                season = 1,
                gameMode = GameMode.GM_1v1,
                gateway = GateWay.Europe,
                players =
                [
                    new PlayerMMrChange
                    {
                        battleTag = battleTag,
                        race = race,
                        atTeamId = atTeamId,
                        updatedMmr = new Mmr { rating = mmr, rd = rd },
                    },
                ],
            },
        };

        await MongoClient
            .GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<MatchFinishedEvent>(nameof(MatchFinishedEvent))
            .InsertOneAsync(finished);
    }

    private Task<PlayerMmrRpTimeline> LoadTimeline(Race race = Race.HU) =>
        Timelines.Find(t => t.Id == $"1_{Player}_@{GateWay.Europe}_{race}_{GameMode.GM_1v1}").FirstOrDefaultAsync();

    [Test]
    public async Task ADayOfGamesBecomesOneEntryCarryingItsCountAndPeak()
    {
        // Finishes below its high point, which is exactly what the old handler lost by
        // keeping only the last game of the day.
        await GivenMatch(Yesterday.AddHours(1), mmr: 1500);
        await GivenMatch(Yesterday.AddHours(2), mmr: 1600);
        await GivenMatch(Yesterday.AddHours(3), mmr: 1550);

        await _job.RunAsync(_context, CancellationToken.None);

        var timeline = await LoadTimeline();
        var entry = timeline.MmrRpAtDates.Single();
        Assert.That(entry.Mmr, Is.EqualTo(1550), "the entry should close on the day's last game");
        Assert.That(entry.PeakMmr, Is.EqualTo(1600));
        Assert.That(entry.GamesOrDefault(timeline.SchemaVersion), Is.EqualTo(3));
    }

    [Test]
    public async Task ASettledRatingStoresNoDeviationAndAnUnsettledOneDoes()
    {
        await GivenMatch(Yesterday.AddHours(1), mmr: 1500, rd: 400);
        await GivenMatch(Yesterday.AddDays(-1).AddHours(1), mmr: 1400, rd: 80);

        await _job.RunAsync(_context, CancellationToken.None);

        var entries = (await LoadTimeline()).MmrRpAtDates.OrderBy(e => e.Date).ToList();
        Assert.That(entries[0].WasCalibrating, Is.False, "an RD under the threshold says nothing and isn't stored");
        Assert.That(entries[1].WasCalibrating, Is.True);
    }

    [Test]
    public async Task ArrangedTeamGamesAreSkippedJustAsTheLiveHandlerSkipsThem()
    {
        await GivenMatch(Yesterday.AddHours(1), mmr: 1500, atTeamId: "team-1");

        await _job.RunAsync(_context, CancellationToken.None);

        Assert.That(await LoadTimeline(), Is.Null);
    }

    [Test]
    public async Task RacesAreKeptOnSeparateTimelines()
    {
        await GivenMatch(Yesterday.AddHours(1), mmr: 1500, race: Race.HU);
        await GivenMatch(Yesterday.AddHours(2), mmr: 1700, race: Race.OC);

        await _job.RunAsync(_context, CancellationToken.None);

        Assert.That((await LoadTimeline(Race.HU)).MmrRpAtDates.Single().Mmr, Is.EqualTo(1500));
        Assert.That((await LoadTimeline(Race.OC)).MmrRpAtDates.Single().Mmr, Is.EqualTo(1700));
    }

    [Test]
    public async Task RunningTwiceProducesTheSameResult()
    {
        await GivenMatch(Yesterday.AddHours(1), mmr: 1500);
        await GivenMatch(Yesterday.AddHours(2), mmr: 1600);

        await _job.RunAsync(_context, CancellationToken.None);
        await _job.RunAsync(new FakeAdminJobContext(), CancellationToken.None);

        var entry = (await LoadTimeline()).MmrRpAtDates.Single();
        Assert.That(entry.GamesOrDefault(1), Is.EqualTo(2), "a rerun must rebuild the day, not add to it");
        Assert.That(entry.PeakMmr, Is.EqualTo(1600));
    }

    [Test]
    public async Task AnExistingTimelineWrittenByTheOldHandlerIsRebuiltInPlace()
    {
        // What the old handler left behind: the day's last game only, no games count,
        // no peak, and no schema version.
        await Timelines.InsertOneAsync(new PlayerMmrRpTimeline(Player, Race.HU, GateWay.Europe, 1, GameMode.GM_1v1)
        {
            MmrRpAtDates = [new MmrRpAtDate(1550, null, Yesterday.AddHours(3))],
        });
        await GivenMatch(Yesterday.AddHours(1), mmr: 1500);
        await GivenMatch(Yesterday.AddHours(2), mmr: 1600);
        await GivenMatch(Yesterday.AddHours(3), mmr: 1550);

        await _job.RunAsync(_context, CancellationToken.None);

        var timeline = await LoadTimeline();
        Assert.That(timeline.MmrRpAtDates, Has.Count.EqualTo(1), "the stale entry should be replaced, not joined");
        Assert.That(timeline.MmrRpAtDates.Single().PeakMmr, Is.EqualTo(1600));
        Assert.That(timeline.SchemaVersion, Is.EqualTo(PlayerMmrRpTimeline.CurrentSchemaVersion));
    }

    [Test]
    public async Task DaysOtherThanTheRebuiltOneAreLeftAlone()
    {
        // A day with no match events behind it - the backfill has nothing to say about
        // it and must not delete it.
        var orphanDay = Yesterday.AddDays(-30);
        await Timelines.InsertOneAsync(new PlayerMmrRpTimeline(Player, Race.HU, GateWay.Europe, 1, GameMode.GM_1v1)
        {
            MmrRpAtDates = [new MmrRpAtDate(1200, null, orphanDay)],
        });
        await GivenMatch(Yesterday.AddHours(1), mmr: 1500);

        await _job.RunAsync(_context, CancellationToken.None);

        var dates = (await LoadTimeline()).MmrRpAtDates.Select(e => e.Date.Date).ToList();
        Assert.That(dates, Does.Contain(orphanDay.Date));
        Assert.That(dates, Does.Contain(Yesterday.Date));
    }

    [Test]
    public async Task TodaysGamesAreLeftToTheLiveHandler()
    {
        await GivenMatch(DateTimeOffset.UtcNow, mmr: 1500);

        await _job.RunAsync(_context, CancellationToken.None);

        Assert.That(await LoadTimeline(), Is.Null,
            "rewriting today would drop games the live handler adds while the job runs");
    }

    [Test]
    public async Task UntouchedTimelinesAreNotClaimedToBeRebuilt()
    {
        // No match events behind it at all, so its entries can't be verified and its
        // version must stay where it is.
        await Timelines.InsertOneAsync(new PlayerMmrRpTimeline("ghost#1", Race.HU, GateWay.Europe, 1, GameMode.GM_1v1)
        {
            MmrRpAtDates = [new MmrRpAtDate(1200, null, Yesterday)],
        });
        await GivenMatch(Yesterday.AddHours(1), mmr: 1500);

        await _job.RunAsync(_context, CancellationToken.None);

        var ghost = await Timelines.Find(t => t.Id.StartsWith("1_ghost#1")).FirstAsync();
        Assert.That(ghost.SchemaVersion, Is.Zero);
        Assert.That((await LoadTimeline()).SchemaVersion, Is.EqualTo(PlayerMmrRpTimeline.CurrentSchemaVersion));
    }

    [Test]
    public async Task TheMarkerIsClearedSoItNeverReachesClients()
    {
        await GivenMatch(Yesterday.AddHours(1), mmr: 1500);

        await _job.RunAsync(_context, CancellationToken.None);

        Assert.That((await LoadTimeline()).BackfillPending, Is.Null);
    }

    [Test]
    public async Task ResumingStartsFromTheCheckpointRatherThanTheBeginning()
    {
        await GivenMatch(Yesterday.AddDays(-5), mmr: 1400);
        await GivenMatch(Yesterday.AddHours(1), mmr: 1500);

        var resumed = new FakeAdminJobContext
        {
            Checkpoint = new BsonDocument("nextDay", DateOnly.FromDateTime(Yesterday.UtcDateTime).DayNumber),
        };
        await _job.RunAsync(resumed, CancellationToken.None);

        var dates = (await LoadTimeline()).MmrRpAtDates.Select(e => e.Date.Date).ToList();
        Assert.That(dates, Is.EquivalentTo(new[] { Yesterday.Date }), "the skipped day should not have been rebuilt");
    }

    [Test]
    public async Task NoMatchHistoryIsNotAnError()
    {
        await _job.RunAsync(_context, CancellationToken.None);

        Assert.That(_context.Messages, Does.Contain("No match events to rebuild from"));
    }
}

/// <summary>Records what a job reports, and hands back a checkpoint the test chose.</summary>
public class FakeAdminJobContext : IAdminJobContext
{
    public BsonDocument Checkpoint { get; set; }
    public long ItemsProcessed { get; private set; }
    public List<string> Messages { get; } = [];

    public Task Report(long current, long total, string message = null, BsonDocument checkpoint = null)
    {
        if (message != null)
        {
            Messages.Add(message);
        }

        if (checkpoint != null)
        {
            Checkpoint = checkpoint;
        }

        return Task.CompletedTask;
    }

    public void AddItems(long count) => ItemsProcessed += count;

    public Task Pace(CancellationToken cancellationToken) => Task.CompletedTask;
}
