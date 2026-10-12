using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.Admin.ReplayExport;
using W3ChampionsStatisticService.Matches;

namespace WC3ChampionsStatisticService.Tests.Admin.ReplayExport;

/// <summary>
/// The time limits and the random pool of the search, against an isolated mongod (Mongo2Go) with test
/// commands on. Failpoints make Mongo answer as if a query hit its time limit.
/// </summary>
[TestFixture]
public class ReplayExportSearchGuardTests
{
    private MongoDbRunner _runner;
    private MongoClient _client;
    private ReplayExportRepository _repository;

    [SetUp]
    public async Task SetUp()
    {
        _runner = MongoDbRunner.Start(additionalMongodArguments: "--setParameter enableTestCommands=1");
        _client = new MongoClient(_runner.ConnectionString);
        _repository = new ReplayExportRepository(_client, new NoPatches(), null, null);

        var games = Enumerable.Range(1, 30).Select(i => new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            { "GameMode", (int)GameMode.GM_1v1 },
            { "Season", 23 },
            { "FloMatchId", i },
            { "Teams", new BsonArray() },
        });
        await _client.GetDatabase("W3Champions-Statistic-Service").GetCollection<BsonDocument>("Matchup").InsertManyAsync(games);
    }

    [TearDown]
    public async Task TearDown()
    {
        await FailPoint("off");
        _runner.Dispose();
    }

    [Test]
    public async Task CountTimeLimit_ShowsTheGamesFoundAsAMinimum()
    {
        // CountDocuments runs as an aggregate; the list runs as a find.
        await FailPoint("alwaysOn", "aggregate");

        var preview = await _repository.Preview(new ReplayExportFilter { GameMode = GameMode.GM_1v1, Quantity = 10 }, []);

        Assert.AreEqual(10, preview.Matches.Count);
        Assert.AreEqual(10, preview.TotalMatching);
        Assert.IsTrue(preview.TotalIsCapped);
    }

    [Test]
    public void SearchTimeLimit_StopsTheSearch()
    {
        FailPoint("alwaysOn", "find").GetAwaiter().GetResult();

        Assert.ThrowsAsync<MongoExecutionTimeoutException>(() =>
            _repository.Preview(new ReplayExportFilter { GameMode = GameMode.GM_1v1, Quantity = 10 }, []));
    }

    [Test]
    public async Task Random_PicksDistinctMatchingGamesNewestFirst()
    {
        var preview = await _repository.Preview(new ReplayExportFilter { GameMode = GameMode.GM_1v1, Quantity = 12, Order = ReplayExportOrder.Random }, []);

        Assert.AreEqual(12, preview.MatchIds.Count);
        Assert.AreEqual(12, preview.MatchIds.Distinct().Count());
        CollectionAssert.AreEqual(preview.MatchIds.OrderByDescending(id => id), preview.MatchIds);
        CollectionAssert.AreEqual(preview.MatchIds, preview.Matches.Select(m => m.Id));
        Assert.AreEqual(30, preview.TotalMatching);
        Assert.IsFalse(preview.TotalIsCapped);
    }

    [TestCase(ReplayExportOrder.Newest)]
    [TestCase(ReplayExportOrder.Oldest)]
    public async Task Preview_ListsTheIdsOfAllGamesAndTheGamesOfTheFirstPage(ReplayExportOrder order)
    {
        var preview = await _repository.Preview(new ReplayExportFilter { GameMode = GameMode.GM_1v1, Order = order }, []);

        var all = await _client.GetDatabase("W3Champions-Statistic-Service").GetCollection<BsonDocument>("Matchup")
            .Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        var expected = all.Select(d => d["_id"].AsObjectId);
        expected = order == ReplayExportOrder.Oldest ? expected.OrderBy(id => id) : expected.OrderByDescending(id => id);

        CollectionAssert.AreEqual(expected.Select(id => id.ToString()), preview.MatchIds);
        CollectionAssert.AreEqual(preview.MatchIds.Take(ReplayExportRepository.FirstPageSize), preview.Matches.Select(m => m.Id));
        Assert.AreEqual(30, preview.TotalMatching);
    }

    [Test]
    public async Task LoadByIds_GivesTheGamesInTheOrderOfThePage()
    {
        var preview = await _repository.Preview(new ReplayExportFilter { GameMode = GameMode.GM_1v1 }, []);
        var page = preview.MatchIds.Skip(25).Concat(preview.MatchIds.Take(2)).Select(ObjectId.Parse).ToList();

        var matches = await _repository.LoadByIds(page);

        CollectionAssert.AreEqual(page.Select(id => id.ToString()), matches.Select(m => m.Id));
    }

    [Test]
    public async Task LoadByIds_HidesTheMmrThatThePublicPagesHide()
    {
        var game = new Matchup
        {
            Id = ObjectId.GenerateNewId(),
            FloMatchId = 99,
            GameMode = GameMode.GM_1v1,
            Teams =
            [
                new Team { Players = [new PlayerOverviewMatches { BattleTag = "sure#1", OldMmr = 1800, OldRankDeviation = 100 }] },
                new Team { Players = [new PlayerOverviewMatches { BattleTag = "new#2", OldMmr = 2400, OldRankDeviation = 300 }] },
            ],
        };
        await _client.GetDatabase("W3Champions-Statistic-Service").GetCollection<Matchup>("Matchup").InsertOneAsync(game);

        var match = (await _repository.LoadByIds([game.Id])).Single();

        Assert.AreEqual(1800, match.Teams[0].Players[0].OldMmr);
        Assert.IsNull(match.Teams[1].Players[0].OldMmr);
        Assert.AreEqual(1800, match.AverageMmr);
    }

    /// <summary>Makes the commands fail with MaxTimeMSExpired (code 50), as a query that hit its time limit.</summary>
    private Task FailPoint(string mode, params string[] commands)
    {
        var command = new BsonDocument { { "configureFailPoint", "failCommand" }, { "mode", mode } };
        if (commands.Length > 0) command.Add("data", new BsonDocument { { "errorCode", 50 }, { "failCommands", new BsonArray(commands) } });
        return _client.GetDatabase("admin").RunCommandAsync<BsonDocument>(command);
    }

    private class NoPatches : IPatchRepository
    {
        public Task<string> GetPatchVersionFromDate(System.DateTime date) => Task.FromResult<string>(null);

        public Task<List<Patch>> LoadPatches() => Task.FromResult(new List<Patch>());
    }
}
