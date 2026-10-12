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
/// The player filter, against an isolated mongod (Mongo2Go): it uses MatchRepository's text index when it
/// exists, and it gives the same games without it.
/// </summary>
[TestFixture]
public class ReplayExportPlayerSearchTests
{
    private const string Database = "W3Champions-Statistic-Service";

    private MongoDbRunner _runner;
    private MongoClient _client;
    private IMongoCollection<BsonDocument> _matches;
    private ReplayExportRepository _repository;
    private List<ObjectId> _playerGames;

    [SetUp]
    public async Task SetUp()
    {
        _runner = MongoDbRunner.Start();
        _client = new MongoClient(_runner.ConnectionString);
        _matches = _client.GetDatabase(Database).GetCollection<BsonDocument>("Matchup");
        _repository = new ReplayExportRepository(_client, new NoPatches(), null, null);

        _playerGames =
        [
            await Insert(["Luthal#29940"], ["Zorluvex#1"]),
            await Insert(["Other#2"], ["Luthal#29940"]),
            await Insert(["Mate#3", "Luthal#29940"], ["Foe#4", "Foe#5"], GameMode.GM_2v2_AT),
        ];
        // Tags that contain the tag, or that the tag contains: not the player.
        await Insert(["Luthal#299401"], ["Zorluvex#1"]);
        await Insert(["xLuthal#29940"], ["Zorluvex#1"]);
        await Insert(["Luthal#2994"], ["Zorluvex#1"]);
        // No FLO game, so no replay.
        await Insert(["Luthal#29940"], ["Zorluvex#1"], floMatchId: null);
    }

    [TearDown]
    public void TearDown() => _runner.Dispose();

    [TestCase(ReplayExportOrder.Newest)]
    [TestCase(ReplayExportOrder.Oldest)]
    [TestCase(ReplayExportOrder.Random)]
    public async Task Player_UsesTheTextIndexAndFindsOnlyThatPlayer(ReplayExportOrder order)
    {
        await CreateTextIndex();
        var database = _client.GetDatabase(Database);
        await database.RunCommandAsync<BsonDocument>(new BsonDocument("profile", 2));

        var preview = await _repository.Preview(Filter("LUTHAL#29940", order), []);

        await database.RunCommandAsync<BsonDocument>(new BsonDocument("profile", 0));
        var plans = await database.GetCollection<BsonDocument>("system.profile")
            .Find(new BsonDocument { { "ns", $"{Database}.Matchup" }, { "planSummary", new BsonDocument("$exists", true) } })
            .ToListAsync();
        var searches = plans.Where(p => !p["planSummary"].AsString.Contains("_id: 1")).ToList();

        Assert.IsTrue(MatchRepositoryHasPlayerTextIndex());
        CollectionAssert.AreEquivalent(_playerGames.Take(2), preview.Matches.Select(m => ObjectId.Parse(m.Id)));
        Assert.AreEqual(2, preview.TotalMatching);
        Assert.IsNotEmpty(searches);
        Assert.IsTrue(searches.All(p => p["planSummary"].AsString.Contains("_fts")), string.Join("\n", searches.Select(p => p["planSummary"])));
    }

    [Test]
    public async Task Player_WithoutTheTextIndex_FindsTheSameGames()
    {
        var preview = await _repository.Preview(Filter("luthal#29940", ReplayExportOrder.Newest), []);

        Assert.IsFalse(MatchRepositoryHasPlayerTextIndex());
        CollectionAssert.AreEqual(_playerGames.Take(2).Reverse(), preview.Matches.Select(m => ObjectId.Parse(m.Id)));
        Assert.AreEqual(2, preview.TotalMatching);
    }

    [Test]
    public async Task Player_InATeamGame()
    {
        await CreateTextIndex();

        var preview = await _repository.Preview(new ReplayExportFilter { GameMode = GameMode.GM_2v2_AT, BattleTag = "Luthal#29940", Quantity = 10 }, []);

        CollectionAssert.AreEqual(new[] { _playerGames[2] }, preview.Matches.Select(m => ObjectId.Parse(m.Id)));
    }

    [Test]
    public void PlayerPhrase_IsTheWholeTagInQuotes()
    {
        Assert.AreEqual("\"Luthal#29940\"", ReplayExportQuery.PlayerPhrase("Luthal#29940"));
        Assert.AreEqual("\"ab\"", ReplayExportQuery.PlayerPhrase("a\"\\b"));
    }

    private static ReplayExportFilter Filter(string battleTag, ReplayExportOrder order) =>
        new() { GameMode = GameMode.GM_1v1, BattleTag = battleTag, Quantity = 10, Order = order };

    private bool MatchRepositoryHasPlayerTextIndex() =>
        ReplayExportRepository.HasPlayerTextIndex(_client.GetDatabase(Database).GetCollection<Matchup>("Matchup")).GetAwaiter().GetResult();

    /// <summary>The same index as MatchRepository.EnsureIndexesAsync.</summary>
    private Task CreateTextIndex() =>
        _matches.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys
            .Text("Team1Players").Text("Team2Players").Text("Team3Players").Text("Team4Players")));

    private async Task<ObjectId> Insert(string[] team1, string[] team2, GameMode gameMode = GameMode.GM_1v1, int? floMatchId = 1)
    {
        var id = ObjectId.GenerateNewId();
        await _matches.InsertOneAsync(new BsonDocument
        {
            { "_id", id },
            { "GameMode", (int)gameMode },
            { "Season", 23 },
            { "FloMatchId", floMatchId.HasValue ? floMatchId.Value : BsonNull.Value },
            { "Teams", new BsonArray { Team(team1), Team(team2) } },
            { "Team1Players", string.Join(";", team1) },
            { "Team2Players", string.Join(";", team2) },
        });
        return id;
    }

    private static BsonDocument Team(string[] players) =>
        new("Players", new BsonArray(players.Select(p => new BsonDocument("BattleTag", p))));

    private class NoPatches : IPatchRepository
    {
        public Task<string> GetPatchVersionFromDate(System.DateTime date) => Task.FromResult<string>(null);

        public Task<List<Patch>> LoadPatches() => Task.FromResult(new List<Patch>());
    }
}
