using NUnit.Framework;
using W3ChampionsStatisticService.Friends;

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
        // duplicate insertion code here
        var duplicateRequests = new FriendRequest("TestPlayer#1234", "TestFriend#5678");

        _friendRequestCache.Insert(duplicateRequests);
        _friendRequestCache.Insert(duplicateRequests);
        Assert.DoesNotThrowAsync(async () => await _friendRequestCache.LoadFriendRequest(duplicateRequests));
    }
}
