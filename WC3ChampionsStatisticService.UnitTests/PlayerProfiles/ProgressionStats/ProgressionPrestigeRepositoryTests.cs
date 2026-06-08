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
    private MongoClient _mongoClient;
    private ProgressionPrestigeRepository _repository;

    [SetUp]
    public void Setup()
    {
        _runner = MongoDbRunner.Start();
        _mongoClient = new MongoClient(_runner.ConnectionString);
        _repository = new ProgressionPrestigeRepository(_mongoClient);
    }

    [TearDown]
    public void TearDown() => _runner.Dispose();

    [Test]
    public async Task UpsertThenLoad_RoundTrips_IncludingDateTimeOffset()
    {
        var prestige = ProgressionPrestige.Create("round#1");
        var achievedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        prestige.RecordPeak(GameMode.GM_1v1, Race.HU,
            new PeakRank { League = 3, Division = 1, Points = 50, Season = 1, AchievedAt = achievedAt });

        await _repository.UpsertPrestige(prestige);
        var loaded = await _repository.LoadPrestige("round#1");

        Assert.IsNotNull(loaded);
        Assert.AreEqual("round#1", loaded.Id);
        Assert.AreEqual(3, loaded.Peaks[0].AllTimePeak.League);
        Assert.AreEqual(achievedAt, loaded.Peaks[0].AllTimePeak.AchievedAt);
    }

    [Test]
    public async Task LoadMissing_ReturnsNull()
    {
        Assert.IsNull(await _repository.LoadPrestige("nobody#1"));
    }

    [Test]
    public async Task Upsert_IsReplaceNotDuplicate()
    {
        var p1 = ProgressionPrestige.Create("dup#1");
        p1.RecordPeak(GameMode.GM_1v1, null,
            new PeakRank { League = 5, Division = 1, Points = 0, Season = 1, AchievedAt = DateTimeOffset.UnixEpoch });
        await _repository.UpsertPrestige(p1);

        // Mutate to a higher rank (Gold -> Diamond) before the second upsert so the replace
        // is proven to carry the mutation, not just dedup.
        p1.RecordPeak(GameMode.GM_1v1, null,
            new PeakRank { League = 3, Division = 1, Points = 50, Season = 1, AchievedAt = DateTimeOffset.UnixEpoch });
        await _repository.UpsertPrestige(p1);

        var collection = _mongoClient.GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<ProgressionPrestige>("ProgressionPrestige");
        Assert.AreEqual(1, await collection.CountDocumentsAsync(FilterDefinition<ProgressionPrestige>.Empty));

        var loaded = await _repository.LoadPrestige("dup#1");
        Assert.AreEqual(3, loaded.Peaks[0].AllTimePeak.League);
    }
}
