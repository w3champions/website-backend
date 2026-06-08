using System;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Driver;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles.ProgressionStats;

[TestFixture]
public class ProgressionPrestigeRepositoryTests
{
    private MongoDbRunner _runner;
    private MongoClient _client;

    [SetUp]
    public void SetUp()
    {
        _runner = MongoDbRunner.Start();
        _client = new MongoClient(_runner.ConnectionString);
    }

    [TearDown]
    public void TearDown() => _runner.Dispose();

    [Test]
    public async Task UpsertThenLoad_RoundTrips_IncludingDateTimeOffset()
    {
        var repo = new ProgressionPrestigeRepository(_client);
        var prestige = ProgressionPrestige.Create("round#1");
        var achievedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        prestige.RecordPeak(GameMode.GM_1v1, Race.HU,
            new PeakRank { League = 3, Division = 1, Points = 50, Season = 1, AchievedAt = achievedAt });

        await repo.UpsertPrestige(prestige);
        var loaded = await repo.LoadPrestige("round#1");

        Assert.IsNotNull(loaded);
        Assert.AreEqual("round#1", loaded.Id);
        Assert.AreEqual(3, loaded.Peaks[0].AllTimePeak.League);
        Assert.AreEqual(achievedAt, loaded.Peaks[0].AllTimePeak.AchievedAt);
    }

    [Test]
    public async Task LoadMissing_ReturnsNull()
    {
        var repo = new ProgressionPrestigeRepository(_client);
        Assert.IsNull(await repo.LoadPrestige("nobody#1"));
    }

    [Test]
    public async Task Upsert_IsReplaceNotDuplicate()
    {
        var repo = new ProgressionPrestigeRepository(_client);
        var p1 = ProgressionPrestige.Create("dup#1");
        p1.RecordPeak(GameMode.GM_1v1, null,
            new PeakRank { League = 5, Division = 1, Points = 0, Season = 1, AchievedAt = DateTimeOffset.UnixEpoch });
        await repo.UpsertPrestige(p1);
        await repo.UpsertPrestige(p1);

        var collection = _client.GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<ProgressionPrestige>("ProgressionPrestige");
        Assert.AreEqual(1, await collection.CountDocumentsAsync(FilterDefinition<ProgressionPrestige>.Empty));
    }
}
