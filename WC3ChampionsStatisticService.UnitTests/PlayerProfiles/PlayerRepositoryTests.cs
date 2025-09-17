using System.Collections.Generic;
using Mongo2Go;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ladder;
using W3C.Contracts.Matchmaking;
using System.Threading.Tasks;
using Moq;
using System.Threading;

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
}
