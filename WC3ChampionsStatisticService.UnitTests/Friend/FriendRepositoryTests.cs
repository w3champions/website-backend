using System.Collections.Generic;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.Friends;

namespace WC3ChampionsStatisticService.Tests.Friend;

// Per-test Mongo2Go runner (mirrors PlayerProfiles/PlayerRepositoryTests.cs) — deliberately NOT
// IntegrationTestBase, which points at a shared remote Mongo and would cross-contaminate.
[TestFixture]
public class FriendRepositoryTests
{
    private MongoDbRunner _runner;
    private MongoClient _mongoClient;
    private FriendRepository _repository;

    [SetUp]
    public void Setup()
    {
        _runner = MongoDbRunner.Start();
        _mongoClient = new MongoClient(_runner.ConnectionString);
        _repository = new FriendRepository(_mongoClient);
    }

    [TearDown]
    public void TearDown()
    {
        _runner.Dispose();
    }

    [Test]
    public async Task LoadFriendlistOrNull_Missing_ReturnsNull_AndDoesNotInsert()
    {
        var result = await _repository.LoadFriendlistOrNull("nobody#0000");

        Assert.That(result, Is.Null);

        var collection = _mongoClient
            .GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<Friendlist>(nameof(Friendlist));
        var count = await collection.CountDocumentsAsync(FilterDefinition<Friendlist>.Empty);

        // Pins the no-side-effect contract that distinguishes this from LoadFriendlist, which
        // inserts a fresh document on a cache miss.
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task LoadFriendlistOrNull_Existing_ReturnsDocument()
    {
        var friendlist = new Friendlist("peter#123")
        {
            Friends = new List<string> { "A#1", "B#2" },
            BlockedBattleTags = new List<string> { "C#3" }
        };
        var collection = _mongoClient
            .GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<Friendlist>(nameof(Friendlist));
        await collection.InsertOneAsync(friendlist);

        var result = await _repository.LoadFriendlistOrNull("peter#123");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo("peter#123"));
        CollectionAssert.AreEquivalent(new List<string> { "A#1", "B#2" }, result.Friends);
        CollectionAssert.AreEquivalent(new List<string> { "C#3" }, result.BlockedBattleTags);
    }
}
