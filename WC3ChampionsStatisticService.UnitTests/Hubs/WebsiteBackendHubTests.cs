using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http;
using Moq;
using W3ChampionsStatisticService.Hubs;
using W3ChampionsStatisticService.Friends;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.Filters;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using NUnit.Framework;

// Minimal in-memory implementations for testing (no inheritance)
public class TestFriendListCache
{
    private readonly Dictionary<string, Friendlist> _lists = new();
    public void Upsert(Friendlist friendList) => _lists[friendList.Id] = friendList;
    public Task<Friendlist> LoadFriendList(string battleTag) =>
        Task.FromResult(_lists.TryGetValue(battleTag, out var fl) ? fl : null);
}

public class TestFriendRepository
{
    public Task UpsertFriendlist(Friendlist _) => Task.CompletedTask;
}

// Custom handler that implements IFriendCommandHandler
public class TestFriendCommandHandler(TestFriendRepository repo, TestFriendListCache cache, FakeFriendRequestCache friendRequestCache) : IFriendCommandHandler
{
    private readonly TestFriendRepository _repo = repo;
    private readonly TestFriendListCache _cache = cache;
    private readonly FakeFriendRequestCache _friendRequestCache = friendRequestCache;

    public async Task<Friendlist> LoadFriendList(string battleTag)
    {
        var friendList = await _cache.LoadFriendList(battleTag);
        if (friendList == null)
        {
            friendList = new Friendlist(battleTag);
            await UpsertFriendList(friendList);
        }
        return friendList;
    }

    public Task CreateFriendRequest(FriendRequest request) => Task.CompletedTask;
    public Task DeleteFriendRequest(FriendRequest request)
    {
        _friendRequestCache.Delete(request);
        return Task.CompletedTask;
    }

    public async Task<Friendlist> AddFriend(Friendlist friendlist, string battleTag)
    {
        if (!friendlist.Friends.Contains(battleTag))
        {
            friendlist.Friends.Add(battleTag);
        }
        await UpsertFriendList(friendlist);
        return friendlist;
    }

    public async Task<Friendlist> RemoveFriend(Friendlist friendlist, string battleTag)
    {
        friendlist.Friends.Remove(battleTag);
        await UpsertFriendList(friendlist);
        return friendlist;
    }

    public async Task UpsertFriendList(Friendlist friendList)
    {
        await _repo.UpsertFriendlist(friendList);
        _cache.Upsert(friendList);
    }
}

// Fake FriendRequestCache for tests (implements IFriendRequestCache)
public class FakeFriendRequestCache : IFriendRequestCache
{
    private readonly List<FriendRequest> _requests = new();
    public Task<List<FriendRequest>> LoadAllFriendRequests() => Task.FromResult(new List<FriendRequest>(_requests));
    public Task<List<FriendRequest>> LoadSentFriendRequests(string sender) => Task.FromResult(_requests.FindAll(r => r.Sender == sender));
    public Task<List<FriendRequest>> LoadReceivedFriendRequests(string receiver) => Task.FromResult(_requests.FindAll(r => r.Receiver == receiver));
    public Task<FriendRequest> LoadFriendRequest(FriendRequest req) => Task.FromResult(_requests.Find(r => r.Sender == req.Sender && r.Receiver == req.Receiver));
    public Task<bool> FriendRequestExists(FriendRequest req) => Task.FromResult(_requests.Exists(r => r.Sender == req.Sender && r.Receiver == req.Receiver));
    public void Insert(FriendRequest req) { _requests.Add(req); }
    public void Delete(FriendRequest req) { _requests.RemoveAll(r => r.Sender == req.Sender && r.Receiver == req.Receiver); }
    public void AddRequest(FriendRequest req) => _requests.Add(req); // For test setup
}

public class WebsiteBackendHubTests
{
    private Mock<IW3CAuthenticationService> authService;
    private ConnectionMapping connections;
    private Mock<IHttpContextAccessor> contextAccessor;
    private FakeFriendRequestCache friendRequestCache;
    private Mock<IPersonalSettingsRepository> personalSettingsRepo;
    private TestFriendListCache friendListCache;
    private TestFriendRepository friendRepository;
    private TracingService tracingService;
    private Mock<IHubCallerClients> mockClients;
    private Mock<ISingleClientProxy> mockCaller;

    [SetUp]
    public void SetUp()
    {
        authService = new Mock<IW3CAuthenticationService>();
        connections = new ConnectionMapping();
        contextAccessor = new Mock<IHttpContextAccessor>();
        friendRequestCache = new FakeFriendRequestCache();
        personalSettingsRepo = new Mock<IPersonalSettingsRepository>();
        friendListCache = new TestFriendListCache();
        friendRepository = new TestFriendRepository();
        tracingService = new TracingService(new System.Diagnostics.ActivitySource("test"), new Mock<IHttpContextAccessor>().Object);
        mockClients = new Mock<IHubCallerClients>();
        mockCaller = new Mock<ISingleClientProxy>();
        mockClients.Setup(clients => clients.Caller).Returns(mockCaller.Object);
    }

    private WebsiteBackendHub CreateHub(IFriendCommandHandler friendCommandHandler)
    {
        var hub = new WebsiteBackendHub(
            authService.Object,
            connections,
            contextAccessor.Object,
            friendRequestCache,
            personalSettingsRepo.Object,
            friendCommandHandler,
            tracingService
        );
        typeof(Hub).GetProperty("Clients").SetValue(hub, mockClients.Object);
        return hub;
    }

    private void SetHubContext(Hub hub, string connectionId)
    {
        typeof(Hub).GetProperty("Context").SetValue(hub, new HubCallerContextMock(connectionId));
    }

    [Test]
    public async Task BlockPlayer_AddsToBlockedBattleTags_WhenNoFriendRequestExists()
    {
        var friendList = new Friendlist("User#1234");
        friendListCache.Upsert(friendList);
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        var hub = CreateHub(friendCommandHandler);

        var testUser = new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" };
        connections.Add("conn1", testUser);

        SetHubContext(hub, "conn1");

        await hub.BlockPlayer("Blocked#5678");

        Assert.That(friendList.BlockedBattleTags, Does.Contain("Blocked#5678"));
        mockCaller.Verify(
            c => c.SendCoreAsync(
                It.Is<string>(s => s.Contains("FriendResponseData")),
                It.IsAny<object[]>(),
                default
            ),
            Times.Once
        );
    }

    [Test]
    public async Task UnblockFriendRequestsFromPlayer_RemovesFromBlockedBattleTags()
    {
        var friendList = new Friendlist("User#1234");
        friendList.BlockedBattleTags.Add("Blocked#5678");
        friendListCache.Upsert(friendList);
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        var hub = CreateHub(friendCommandHandler);

        var testUser = new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" };
        connections.Add("conn1", testUser);

        SetHubContext(hub, "conn1");

        await hub.UnblockFriendRequestsFromPlayer("Blocked#5678");

        Assert.That(friendList.BlockedBattleTags, Does.Not.Contain("Blocked#5678"));
        mockCaller.Verify(
            c => c.SendCoreAsync(
                It.Is<string>(s => s.Contains("FriendResponseData")),
                It.IsAny<object[]>(),
                default
            ),
            Times.Once
        );
    }

    [Test]
    public async Task RemoveFriend_RemovesFromFriendsList()
    {
        var friendList = new Friendlist("User#1234");
        friendList.Friends.Add("Friend#5678");
        friendListCache.Upsert(friendList);
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        var hub = CreateHub(friendCommandHandler);

        var testUser = new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" };
        connections.Add("conn1", testUser);

        SetHubContext(hub, "conn1");

        await hub.RemoveFriend("Friend#5678");

        Assert.That(friendList.Friends, Does.Not.Contain("Friend#5678"));
        mockCaller.Verify(
            c => c.SendCoreAsync(
                It.Is<string>(s => s.Contains("FriendResponseData")),
                It.IsAny<object[]>(),
                default
            ),
            Times.Once
        );
    }

    [Test]
    public async Task LoadFriendListAndRequestsTraced_SendsFriendListAndRequests()
    {
        var friendList = new Friendlist("User#1234");
        friendListCache.Upsert(friendList);
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        var hub = CreateHub(friendCommandHandler);

        var testUser = new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" };
        connections.Add("conn1", testUser);

        SetHubContext(hub, "conn1");

        await hub.LoadFriendListAndRequestsTraced(new SignalRTraceContextFilter.PreventZeroArgumentHandler());

        mockCaller.Verify(
            c => c.SendCoreAsync(
                It.Is<string>(s => s.Contains("FriendResponseData")),
                It.IsAny<object[]>(),
                default
            ),
            Times.Once
        );
    }

    [Test]
    public async Task MakeFriendRequest_SendsSuccessMessage()
    {
        personalSettingsRepo.Setup(r => r.Find(It.IsAny<string>())).ReturnsAsync(new PersonalSetting("Receiver#1"));

        var friendList = new Friendlist("Sender#1");
        friendListCache.Upsert(friendList);
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        var hub = CreateHub(friendCommandHandler);

        var testUser = new WebSocketUser { BattleTag = "Sender#1", ConnectionId = "conn1" };
        connections.Add("conn1", testUser);

        SetHubContext(hub, "conn1");

        var req = new FriendRequest("Sender#1", "Receiver#1");
        await hub.MakeFriendRequest(req);

        mockCaller.Verify(
            c => c.SendCoreAsync(
                It.Is<string>(s => s.Contains("FriendResponseData")),
                It.IsAny<object[]>(),
                default
            ),
            Times.Once
        );
    }

    [TestCase("AcceptIncomingFriendRequest", "Receiver#1")]
    [TestCase("DenyIncomingFriendRequest", "Receiver#1")]
    [TestCase("DeleteOutgoingFriendRequest", "Sender#1")]
    [TestCase("BlockIncomingFriendRequest", "Receiver#1")]
    public async Task FriendRequestFlow_SendsSuccessMessage(string methodName, string userBattleTag)
    {
        var friendList = new Friendlist(userBattleTag);
        friendListCache.Upsert(friendList);
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        // Add the friend request to the cache so the flow will succeed
        var req = new FriendRequest("Sender#1", "Receiver#1");
        friendRequestCache.AddRequest(req);

        // For Accept and Block, also add the reciprocal request to the cache
        if (methodName == "AcceptIncomingFriendRequest" || methodName == "BlockIncomingFriendRequest")
        {
            friendRequestCache.AddRequest(new FriendRequest("Receiver#1", "Sender#1"));
        }

        var hub = CreateHub(friendCommandHandler);
        var testUser = new WebSocketUser { BattleTag = userBattleTag, ConnectionId = "conn1" };
        connections.Add("conn1", testUser);
        SetHubContext(hub, "conn1");

        var method = typeof(WebsiteBackendHub).GetMethod(methodName);
        await (Task)method.Invoke(hub, new object[] { req });

        mockCaller.Verify(
            c => c.SendCoreAsync(
                It.Is<string>(s => s.Contains("FriendResponseData")),
                It.IsAny<object[]>(),
                default
            ),
            Times.AtLeastOnce
        );

        // Additional assertions for each flow
        switch (methodName)
        {
            case "AcceptIncomingFriendRequest":
                // Both users should be friends with each other
                var receiverList = await friendCommandHandler.LoadFriendList("Receiver#1");
                var senderList = await friendCommandHandler.LoadFriendList("Sender#1");
                Assert.That(receiverList.Friends, Does.Contain("Sender#1"));
                Assert.That(senderList.Friends, Does.Contain("Receiver#1"));
                // Only the original FriendRequest should be removed from cache
                var reqInCache = await friendRequestCache.LoadFriendRequest(req);
                Assert.That(reqInCache, Is.Null);
                break;
            case "DenyIncomingFriendRequest":
            case "DeleteOutgoingFriendRequest":
                // FriendRequest should be removed from cache
                var reqInCache2 = await friendRequestCache.LoadFriendRequest(req);
                Assert.That(reqInCache2, Is.Null);
                break;
            case "BlockIncomingFriendRequest":
                // Sender should be in blocked list
                var receiverListBlock = await friendCommandHandler.LoadFriendList("Receiver#1");
                Assert.That(receiverListBlock.BlockedBattleTags, Does.Contain("Sender#1"));
                // Only the original FriendRequest should be removed from cache
                var reqInCache3 = await friendRequestCache.LoadFriendRequest(req);
                Assert.That(reqInCache3, Is.Null);
                break;
        }
    }

    // Helper mock for HubCallerContext
    private class HubCallerContextMock(string connectionId) : HubCallerContext
    {
        public override string ConnectionId { get; } = connectionId;
        public override string UserIdentifier => null;
        public override ClaimsPrincipal User => null;
        public override IDictionary<object, object> Items { get; } = new Dictionary<object, object>();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override void Abort() { }
    }
}
