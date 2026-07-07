using System.Collections.Generic;
using System.Linq;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ladder;
using W3C.Contracts.Matchmaking;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles;

[TestFixture]
public class PlayerRepositoryTests
{
    private MongoDbRunner _runner;
    private MongoClient _mongoClient;
    private PlayerRepository _repository;

    [SetUp]
    public void Setup()
    {
        _runner = MongoDbRunner.Start();
        _mongoClient = new MongoClient(_runner.ConnectionString);
        _repository = new PlayerRepository(_mongoClient);
    }

    [TearDown]
    public void TearDown()
    {
        _runner.Dispose();
    }

    [Test]
    public void LoadMaxMMR_ReturnsHighestMmr()
    {
        // Arrange: Insert test PlayerOverview documents
        var collection = _mongoClient.GetDatabase("W3Champions-Statistic-Service").GetCollection<PlayerOverview>("PlayerOverview");
        collection.InsertMany(new List<PlayerOverview>
    {
      new PlayerOverview { MMR = 1500, Id = "1", GameMode = GameMode.GM_1v1 },
      new PlayerOverview { MMR = 2000, Id = "2", GameMode = GameMode.GM_1v1  },
      new PlayerOverview { MMR = 3001, Id = "3", GameMode = GameMode.GM_1v1  }
    });

        // Act
        var maxMmr = _repository.LoadMaxMMR(GameMode.GM_1v1);

        // Assert
        Assert.AreEqual(3001, maxMmr);
    }

    [Test]
    public async Task LoadMmrs_ReturnsCorrectMmrs()
    {
        var collection = _mongoClient.GetDatabase("W3Champions-Statistic-Service").GetCollection<PlayerOverview>("PlayerOverview");
        collection.InsertMany(new List<PlayerOverview>
    {
      new PlayerOverview { Id = "a", MMR = 1000, Season = 1, GateWay = GateWay.Europe, GameMode = GameMode.GM_1v1 },
      new PlayerOverview { Id = "b", MMR = 2000, Season = 1, GateWay = GateWay.Europe, GameMode = GameMode.GM_1v1 },
      new PlayerOverview { Id = "c", MMR = 3000, Season = 1, GateWay = GateWay.Europe, GameMode = GameMode.GM_1v1 },
      new PlayerOverview { Id = "d", MMR = 4000, Season = 2, GateWay = GateWay.Europe, GameMode = GameMode.GM_1v1 }
    });

        var mmrs = await _repository.LoadMmrs(1, GateWay.Europe, GameMode.GM_1v1);
        CollectionAssert.AreEquivalent(new List<int> { 1000, 2000, 3000 }, mmrs);
    }

    [Test]
    public async Task LoadMmrs_UsesCacheWithinHour()
    {
        var collection = _mongoClient.GetDatabase("W3Champions-Statistic-Service").GetCollection<PlayerOverview>("PlayerOverview");
        collection.InsertMany(new List<PlayerOverview>
      {
          new PlayerOverview { MMR = 1000, Season = 1, GateWay = GateWay.Europe, GameMode = GameMode.GM_1v1 }
      });

        var mmrs1 = await _repository.LoadMmrs(1, GateWay.Europe, GameMode.GM_1v1);
        // Remove all documents to verify cache is used
        collection.DeleteMany(Builders<PlayerOverview>.Filter.Empty);
        var mmrs2 = await _repository.LoadMmrs(1, GateWay.Europe, GameMode.GM_1v1);
        CollectionAssert.AreEquivalent(mmrs1, mmrs2);
    }

    [Test]
    public void LoadMaxMMR_ReturnsMaxForSpecificGameMode()
    {
        // Arrange
        var collection = _mongoClient.GetDatabase("W3Champions-Statistic-Service").GetCollection<PlayerOverview>("PlayerOverview");
        collection.InsertMany(new List<PlayerOverview>
        {
            new PlayerOverview { Id = "1", MMR = 1000, GameMode = GameMode.GM_1v1 },
            new PlayerOverview { Id = "2", MMR = 2000, GameMode = GameMode.GM_1v1 },
            new PlayerOverview { Id = "3", MMR = 1500, GameMode = GameMode.GM_2v2 }
        });

        // Act
        int maxMmr = _repository.LoadMaxMMR(GameMode.GM_1v1);
        int maxMmr2v2 = _repository.LoadMaxMMR(GameMode.GM_2v2);

        // Assert
        Assert.AreEqual(2000, maxMmr);
        Assert.AreEqual(1500, maxMmr2v2);
    }

    [Test]
    public void LoadMaxMMR_ReturnsMaxAcrossAllGameModes_WhenGameModeIsUndefined()
    {
        // Arrange
        var collection = _mongoClient.GetDatabase("W3Champions-Statistic-Service").GetCollection<PlayerOverview>("PlayerOverview");
        collection.InsertMany(new List<PlayerOverview>
        {
            new PlayerOverview { Id = "1", MMR = 1000, GameMode = GameMode.GM_1v1 },
            new PlayerOverview { Id = "2", MMR = 2000, GameMode = GameMode.GM_1v1 },
            new PlayerOverview { Id = "3", MMR = 1500, GameMode = GameMode.GM_2v2 }
        });

        // Act
        int maxMmr = _repository.LoadMaxMMR(GameMode.Undefined);

        // Assert
        Assert.AreEqual(2000, maxMmr);
    }

    private static PlayerGameModeStatPerGateway CreateStat(
        string battleTag, GateWay gateWay, GameMode gameMode, int season, Race? race, int wins, int losses)
    {
        var stat = PlayerGameModeStatPerGateway.Create(new BattleTagIdCombined(
            new List<PlayerId> { PlayerId.Create(battleTag) }, gateWay, gameMode, season, race));
        for (var i = 0; i < wins; i++) stat.RecordWin(true);
        for (var i = 0; i < losses; i++) stat.RecordWin(false);
        return stat;
    }

    [Test]
    public async Task LoadGameModeStatPerGateway_ByBattleTagAndSeason_ReturnsAllGatewaysAndModes()
    {
        await _repository.UpsertPlayerGameModeStatPerGateway(
            CreateStat("peter#123", GateWay.Europe, GameMode.GM_1v1, 5, Race.HU, wins: 2, losses: 1));
        await _repository.UpsertPlayerGameModeStatPerGateway(
            CreateStat("peter#123", GateWay.America, GameMode.GM_2v2, 5, null, wins: 0, losses: 1));
        await _repository.UpsertPlayerGameModeStatPerGateway(
            CreateStat("peter#123", GateWay.Europe, GameMode.GM_1v1, 4, Race.HU, wins: 9, losses: 9)); // wrong season
        await _repository.UpsertPlayerGameModeStatPerGateway(
            CreateStat("wolf#456", GateWay.Europe, GameMode.GM_1v1, 5, Race.OC, wins: 1, losses: 1)); // other player

        // AT team doc carries BOTH players — must be returned for each member
        var atStat = PlayerGameModeStatPerGateway.Create(new BattleTagIdCombined(
            new List<PlayerId> { PlayerId.Create("peter#123"), PlayerId.Create("wolf#456") },
            GateWay.Europe, GameMode.GM_2v2_AT, 5, null));
        atStat.RecordWin(true);
        atStat.RecordWin(false);
        await _repository.UpsertPlayerGameModeStatPerGateway(atStat);

        var peterStats = await _repository.LoadGameModeStatPerGateway("peter#123", 5);
        var wolfStats = await _repository.LoadGameModeStatPerGateway("wolf#456", 5);

        Assert.AreEqual(3, peterStats.Count);
        Assert.AreEqual(6, peterStats.Sum(s => s.Games)); // 3 + 1 + 2, wrong-season and other-player excluded
        Assert.AreEqual(2, wolfStats.Count);
        Assert.AreEqual(4, wolfStats.Sum(s => s.Games)); // own 2 + AT 2
    }

    [Test]
    public async Task EnsureIndexesAsync_CreatesBattleTagGwSeasonAndGatewaySeasonIndexes_MatchingProdSpec()
    {
        await _repository.EnsureIndexesAsync();

        var collection = _mongoClient
            .GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<PlayerGameModeStatPerGateway>(nameof(PlayerGameModeStatPerGateway));
        var indexes = await (await collection.Indexes.ListAsync()).ToListAsync();
        var indexDump = $"count={indexes.Count}; " + string.Join(" | ", indexes.Select(i => i.ToJson()));

        var battleTagGwSeason = indexes.FirstOrDefault(i =>
            i.GetValue("name", BsonString.Empty).AsString == "battleTag_gw_season");
        Assert.That(battleTagGwSeason, Is.Not.Null, "battleTag_gw_season index missing — got: " + indexDump);
        Assert.That(
            battleTagGwSeason["key"],
            Is.EqualTo(new BsonDocument
            {
                { "PlayerIds.BattleTag", 1 },
                { "GateWay", 1 },
                { "Season", 1 }
            }),
            "battleTag_gw_season keys must match the exact prod spec (PlayerIds.BattleTag, GateWay, Season all ascending)");
        Assert.That(battleTagGwSeason.Contains("unique"), Is.False, "battleTag_gw_season must not be unique (prod isUnique=false)");
        Assert.That(battleTagGwSeason.Contains("sparse"), Is.False, "battleTag_gw_season must not be sparse (prod isSparse=false)");
        Assert.That(battleTagGwSeason.Contains("partialFilterExpression"), Is.False, "battleTag_gw_season must not be partial (prod isPartial=false)");

        var ixGatewaySeason = indexes.FirstOrDefault(i =>
            i.GetValue("name", BsonString.Empty).AsString == "ix_gateway_season");
        Assert.That(ixGatewaySeason, Is.Not.Null, "ix_gateway_season index missing — got: " + indexDump);
        Assert.That(
            ixGatewaySeason["key"],
            Is.EqualTo(new BsonDocument
            {
                { "GateWay", 1 },
                { "Season", -1 }
            }),
            "ix_gateway_season keys must match the exact prod spec (GateWay ascending, Season descending)");
        Assert.That(ixGatewaySeason.Contains("unique"), Is.False, "ix_gateway_season must not be unique (prod isUnique=false)");
        Assert.That(ixGatewaySeason.Contains("sparse"), Is.False, "ix_gateway_season must not be sparse (prod isSparse=false)");
        Assert.That(ixGatewaySeason.Contains("partialFilterExpression"), Is.False, "ix_gateway_season must not be partial (prod isPartial=false)");
    }

    [Test]
    public void EnsureIndexesAsync_IsIdempotent()
    {
        // Identical name + keys + options on the second call must be a no-op, not an
        // IndexKeySpecsConflict — this is what keeps CreateManyAsync safe to run against
        // an environment (like prod) where these indexes already exist out-of-band.
        Assert.DoesNotThrowAsync(async () => await _repository.EnsureIndexesAsync());
        Assert.DoesNotThrowAsync(async () => await _repository.EnsureIndexesAsync());
    }
}
