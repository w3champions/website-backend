using System;
using System.Linq;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin.ReplayExport;

namespace WC3ChampionsStatisticService.Tests.Admin.ReplayExport;

/// <summary>
/// The export history against an isolated in-memory mongod (Mongo2Go), not the shared remote Mongo.
/// </summary>
[TestFixture]
public class ReplayExportHistoryTests
{
    private MongoDbRunner _runner;
    private ReplayExportRepository _repository;

    [SetUp]
    public void SetUp()
    {
        _runner = MongoDbRunner.Start();
        _repository = new ReplayExportRepository(new MongoClient(_runner.ConnectionString), null, null, null);
    }

    [TearDown]
    public void TearDown()
    {
        _runner.Dispose();
    }

    [Test]
    public async Task ExcludedIds_AreTheGamesOfAllOrOfTheSelectedDownloads()
    {
        var first = await Save("Order 1", 3);
        var second = await Save("Order 2", 2);
        var third = await Save("Not selected", 4);
        var otherUser = await Save("Other user", 2, "Other#2");

        var selected = await _repository.LoadExcludedIds(new ReplayExportFilter
        {
            ExcludePastDownloads = true,
            ExcludeExportIds = [first.Id.ToString(), second.Id.ToString(), otherUser.Id.ToString(), "not-an-id"],
        }, "Admin#1");
        var all = await _repository.LoadExcludedIds(new ReplayExportFilter { ExcludePastDownloads = true }, "Admin#1");
        var off = await _repository.LoadExcludedIds(new ReplayExportFilter { ExcludeExportIds = [first.Id.ToString()] }, "Admin#1");

        CollectionAssert.AreEquivalent(first.MatchIds.Concat(second.MatchIds), selected);
        CollectionAssert.AreEquivalent(first.MatchIds.Concat(second.MatchIds).Concat(third.MatchIds), all);
        CollectionAssert.IsEmpty(off);
    }

    [Test]
    public async Task Exports_AreTheUsersOwnNewestFirstWithTheirSize()
    {
        await Save("Order 1", 3);
        await Save("Other user", 1, "Other#2");
        await Save(null, 2);

        var exports = await _repository.LoadExports("Admin#1");

        Assert.AreEqual(2, exports.Count);
        Assert.IsNull(exports[0].Label);
        Assert.AreEqual(2, exports[0].Count);
        Assert.AreEqual("Order 1", exports[1].Label);
        Assert.AreEqual(3, exports[1].Count);
        Assert.AreEqual("Admin#1", exports[1].RequestedBy);

        Assert.AreEqual(3, (await _repository.LoadAllExports(null)).Count);
        Assert.AreEqual("Other#2", (await _repository.LoadAllExports("Other#2")).Single().RequestedBy);
    }

    [Test]
    public async Task RepeatDownload_CountsAndKeepsTheGames()
    {
        var record = await Save("Order 1", 3);

        await _repository.MarkDownloadedAgain(record.Id, "Support#3");
        var loaded = await _repository.LoadExport(record.Id.ToString(), "Admin#1");

        Assert.AreEqual(2, loaded.DownloadCount);
        CollectionAssert.AreEqual(new[] { "Admin#1", "Support#3" }, loaded.Downloads.Select(d => d.By));
        Assert.Greater(loaded.LastDownloadedAt, record.LastDownloadedAt);
        CollectionAssert.AreEqual(record.MatchIds, loaded.MatchIds);
        Assert.IsNull(await _repository.LoadExport("not-an-id", "Admin#1"));
        Assert.IsNull(await _repository.LoadExport(record.Id.ToString(), "Other#2"), "Another user's download");
        Assert.IsNotNull(await _repository.LoadAnyExport(record.Id.ToString()), "Admins can load any download");
    }

    private async Task<ReplayExportRecord> Save(string label, int games, string battleTag = "Admin#1")
    {
        var record = new ReplayExportRecord
        {
            Id = ObjectId.GenerateNewId(),
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            LastDownloadedAt = DateTime.UtcNow.AddMinutes(-5),
            RequestedBy = battleTag,
            Downloads = [new ReplayExportDownload { By = battleTag, At = DateTime.UtcNow.AddMinutes(-5) }],
            Label = label,
            Requested = games,
            MatchIds = Enumerable.Range(0, games).Select(_ => ObjectId.GenerateNewId()).ToList(),
            Filter = new ReplayExportFilter { Seasons = [23] },
        };
        await _repository.SaveExport(record);
        return record;
    }
}
