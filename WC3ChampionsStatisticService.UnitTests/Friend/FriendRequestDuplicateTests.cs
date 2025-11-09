using NUnit.Framework;
using W3ChampionsStatisticService.Friends;
using System.Threading.Tasks;

namespace WC3ChampionsStatisticService.Tests.Friend;

[TestFixture]
public class FriendRequestDuplicateTests : IntegrationTestBase
{
    private FriendRequestCache _friendRequestCache;

    [SetUp]
    public void SetUp()
    {
        _friendRequestCache = new FriendRequestCache(MongoClient);
    }

    [Test]
    public void SimulateDuplicateFriendRequests()
    {
        var duplicateRequests = new FriendRequest("TestPlayer#1234", "TestFriend#5678");

        _friendRequestCache.Insert(duplicateRequests);
        _friendRequestCache.Insert(duplicateRequests);
        Assert.DoesNotThrowAsync(async () => await _friendRequestCache.LoadFriendRequest(duplicateRequests));
    }

    [Test]
    public async Task RemoveDuplicateFriendRequests()
    {
        var duplicateRequests = new FriendRequest("TestPlayer#1234", "TestFriend#5678");

        _friendRequestCache.Insert(duplicateRequests);
        _friendRequestCache.Insert(duplicateRequests);
        var requestList = await _friendRequestCache.LoadAllFriendRequests();
        Assert.That(requestList.Count, Is.EqualTo(2));
        _friendRequestCache.Delete(duplicateRequests);
        Assert.That(requestList.Count, Is.EqualTo(0));
    }
}
