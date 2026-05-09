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
    private Mock<IBattleTagResolver> _battleTagResolverMock;

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
        _battleTagResolverMock = new Mock<IBattleTagResolver>();
        // Default: every BattleTag resolves as canonical (returns input unchanged).
        // Individual tests override this for non-canonical or not-found scenarios.
        _battleTagResolverMock
            .Setup(r => r.ResolveCanonical(It.IsAny<string>()))
            .ReturnsAsync((string input) => input);
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
            tracingService,
            _battleTagResolverMock.Object
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

    [Test]
    public async Task MakeFriendRequest_OverwritesPayloadSenderWithJwtBattleTag()
    {
        // Arrange: attacker sends a request claiming to be "Impersonated#5678", but JWT says "JwtUser#1234"
        var friendCommandHandlerMock = new Mock<IFriendCommandHandler>();
        friendCommandHandlerMock
            .Setup(h => h.LoadFriendList(It.IsAny<string>()))
            .ReturnsAsync((string bt) => new Friendlist(bt));

        personalSettingsRepo
            .Setup(r => r.Find(It.IsAny<string>()))
            .ReturnsAsync(new PersonalSetting("Receiver#9999"));

        // Receiver is canonical so canonicalization passes through
        _battleTagResolverMock
            .Setup(r => r.ResolveCanonical("Receiver#9999"))
            .ReturnsAsync("Receiver#9999");

        var hub = CreateHub(friendCommandHandlerMock.Object);
        SetHubContext(hub, "connection-id-1");
        connections.Add("connection-id-1", new WebSocketUser { BattleTag = "JwtUser#1234", ConnectionId = "connection-id-1" });

        FriendRequest capturedReq = null;
        friendCommandHandlerMock
            .Setup(h => h.CreateFriendRequest(It.IsAny<FriendRequest>()))
            .Callback<FriendRequest>(r => capturedReq = r)
            .Returns(Task.CompletedTask);

        var req = new FriendRequest("Impersonated#5678", "Receiver#9999"); // attacker-controllable Sender

        // Act
        await hub.MakeFriendRequest(req);

        // Assert: Sender must be overwritten with JWT BattleTag
        Assert.IsNotNull(capturedReq, "CreateFriendRequest should have been called.");
        Assert.AreEqual("JwtUser#1234", capturedReq.Sender, "Sender must be overwritten with JWT BattleTag.");
    }

    [Test]
    public async Task MakeFriendRequest_NonCanonicalReceiver_EmitsErrorEvent()
    {
        // Arrange: client sends a non-canonical (lowercase) Receiver
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        _battleTagResolverMock
            .Setup(r => r.ResolveCanonical("torren#11438"))
            .ReturnsAsync("TORREN#11438"); // canonical differs — non-canonical input

        var hub = CreateHub(friendCommandHandler);
        SetHubContext(hub, "connection-id-1");
        connections.Add("connection-id-1", new WebSocketUser { BattleTag = "Sender#1234", ConnectionId = "connection-id-1" });

        var req = new FriendRequest("Sender#1234", "torren#11438");

        // Act
        await hub.MakeFriendRequest(req);

        // Assert: error event emitted, handler NOT called
        mockCaller.Verify(c => c.SendCoreAsync("BattleTagResolutionError", It.IsAny<object[]>(), default), Times.Once);
    }

    [Test]
    public async Task AcceptIncomingFriendRequest_OverwritesPayloadReceiverWithJwtBattleTag()
    {
        // Arrange: attacker sends a request with forged Receiver, JWT says "JwtUser#1234"
        var friendCommandHandlerMock = new Mock<IFriendCommandHandler>();
        friendCommandHandlerMock
            .Setup(h => h.LoadFriendList(It.IsAny<string>()))
            .ReturnsAsync((string bt) => new Friendlist(bt));
        friendCommandHandlerMock
            .Setup(h => h.AddFriend(It.IsAny<Friendlist>(), It.IsAny<string>()))
            .ReturnsAsync((Friendlist fl, string bt) => fl);
        friendCommandHandlerMock
            .Setup(h => h.DeleteFriendRequest(It.IsAny<FriendRequest>()))
            .Returns(Task.CompletedTask);

        // Use a mock IFriendRequestCache so we can fully control LoadFriendRequest
        var friendRequestCacheMock = new Mock<IFriendRequestCache>();

        // The pinned req will have {Sender="Sender#9999", Receiver="JwtUser#1234"}
        var storedReq = new FriendRequest("Sender#9999", "JwtUser#1234");
        // Return the stored request when the correct (JWT-pinned) parameters are used
        friendRequestCacheMock
            .Setup(c => c.LoadFriendRequest(It.IsAny<FriendRequest>()))
            .ReturnsAsync((FriendRequest r) =>
                r.Sender == "Sender#9999" && r.Receiver == "JwtUser#1234" ? storedReq : null);
        friendRequestCacheMock
            .Setup(c => c.LoadSentFriendRequests(It.IsAny<string>()))
            .ReturnsAsync(new System.Collections.Generic.List<FriendRequest>());
        friendRequestCacheMock
            .Setup(c => c.LoadReceivedFriendRequests(It.IsAny<string>()))
            .ReturnsAsync(new System.Collections.Generic.List<FriendRequest>());

        // Sender is canonical so canonicalization passes through
        _battleTagResolverMock
            .Setup(r => r.ResolveCanonical("Sender#9999"))
            .ReturnsAsync("Sender#9999");

        // Build hub with the mock IFriendRequestCache instead of friendRequestCache
        var hub = new WebsiteBackendHub(
            authService.Object,
            connections,
            contextAccessor.Object,
            friendRequestCacheMock.Object,
            personalSettingsRepo.Object,
            friendCommandHandlerMock.Object,
            tracingService,
            _battleTagResolverMock.Object
        );
        typeof(Hub).GetProperty("Clients").SetValue(hub, mockClients.Object);
        SetHubContext(hub, "connection-id-1");
        connections.Add("connection-id-1", new WebSocketUser { BattleTag = "JwtUser#1234", ConnectionId = "connection-id-1" });

        var req = new FriendRequest("Sender#9999", "Impersonated#5678"); // forged Receiver

        // Act
        await hub.AcceptIncomingFriendRequest(req);

        // Assert: LoadFriendRequest was called with the JWT-pinned Receiver (Sender#9999 + JwtUser#1234)
        // If the Receiver was NOT pinned (still "Impersonated#5678"), the mock returns null and
        // AcceptIncomingFriendRequest throws ValidationException — success response would not be sent.
        friendRequestCacheMock.Verify(
            c => c.LoadFriendRequest(It.Is<FriendRequest>(r => r.Sender == "Sender#9999" && r.Receiver == "JwtUser#1234")),
            Times.Once,
            "LoadFriendRequest must be called with the JWT-pinned Receiver."
        );
    }

    [Test]
    public async Task DenyIncomingFriendRequest_OverwritesPayloadReceiverWithJwtBattleTag()
    {
        // Arrange: attacker sends a request with forged Receiver, JWT says "JwtUser#1234"
        var friendCommandHandlerMock = new Mock<IFriendCommandHandler>();
        friendCommandHandlerMock
            .Setup(h => h.LoadFriendList(It.IsAny<string>()))
            .ReturnsAsync((string bt) => new Friendlist(bt));
        friendCommandHandlerMock
            .Setup(h => h.DeleteFriendRequest(It.IsAny<FriendRequest>()))
            .Returns(Task.CompletedTask);

        var friendRequestCacheMock = new Mock<IFriendRequestCache>();
        var storedReq = new FriendRequest("Sender#9999", "JwtUser#1234");
        friendRequestCacheMock
            .Setup(c => c.LoadSentFriendRequests(It.IsAny<string>()))
            .ReturnsAsync(new System.Collections.Generic.List<FriendRequest>());
        friendRequestCacheMock
            .Setup(c => c.LoadReceivedFriendRequests(It.IsAny<string>()))
            .ReturnsAsync(new System.Collections.Generic.List<FriendRequest>());
        friendRequestCacheMock
            .Setup(c => c.LoadFriendRequest(It.IsAny<FriendRequest>()))
            .ReturnsAsync((FriendRequest r) =>
                r.Sender == "Sender#9999" && r.Receiver == "JwtUser#1234" ? storedReq : null);

        _battleTagResolverMock
            .Setup(r => r.ResolveCanonical("Sender#9999"))
            .ReturnsAsync("Sender#9999");

        var hub = new WebsiteBackendHub(
            authService.Object,
            connections,
            contextAccessor.Object,
            friendRequestCacheMock.Object,
            personalSettingsRepo.Object,
            friendCommandHandlerMock.Object,
            tracingService,
            _battleTagResolverMock.Object
        );
        typeof(Hub).GetProperty("Clients").SetValue(hub, mockClients.Object);
        SetHubContext(hub, "connection-id-1");
        connections.Add("connection-id-1", new WebSocketUser { BattleTag = "JwtUser#1234", ConnectionId = "connection-id-1" });

        var req = new FriendRequest("Sender#9999", "Impersonated#5678"); // forged Receiver

        // Act
        await hub.DenyIncomingFriendRequest(req);

        // Assert: LoadFriendRequest was called with the JWT-pinned Receiver
        // If the Receiver was NOT pinned (still "Impersonated#5678"), the mock returns null and
        // DenyIncomingFriendRequest throws ValidationException — the success path is not reached.
        friendRequestCacheMock.Verify(
            c => c.LoadFriendRequest(It.Is<FriendRequest>(r => r.Sender == "Sender#9999" && r.Receiver == "JwtUser#1234")),
            Times.Once,
            "LoadFriendRequest must be called with the JWT-pinned Receiver."
        );
    }

    [Test]
    public async Task BlockIncomingFriendRequest_OverwritesPayloadReceiverWithJwtBattleTag()
    {
        // Arrange: attacker sends a request with forged Receiver, JWT says "JwtUser#1234"
        var friendCommandHandlerMock = new Mock<IFriendCommandHandler>();
        friendCommandHandlerMock
            .Setup(h => h.LoadFriendList(It.IsAny<string>()))
            .ReturnsAsync((string bt) => new Friendlist(bt));
        friendCommandHandlerMock
            .Setup(h => h.UpsertFriendList(It.IsAny<Friendlist>()))
            .Returns(Task.CompletedTask);
        friendCommandHandlerMock
            .Setup(h => h.DeleteFriendRequest(It.IsAny<FriendRequest>()))
            .Returns(Task.CompletedTask);

        var friendRequestCacheMock = new Mock<IFriendRequestCache>();
        var storedReq = new FriendRequest("Sender#9999", "JwtUser#1234");
        friendRequestCacheMock
            .Setup(c => c.LoadSentFriendRequests(It.IsAny<string>()))
            .ReturnsAsync(new System.Collections.Generic.List<FriendRequest>());
        friendRequestCacheMock
            .Setup(c => c.LoadReceivedFriendRequests(It.IsAny<string>()))
            .ReturnsAsync(new System.Collections.Generic.List<FriendRequest>());
        friendRequestCacheMock
            .Setup(c => c.LoadFriendRequest(It.IsAny<FriendRequest>()))
            .ReturnsAsync((FriendRequest r) =>
                r.Sender == "Sender#9999" && r.Receiver == "JwtUser#1234" ? storedReq : null);

        _battleTagResolverMock
            .Setup(r => r.ResolveCanonical("Sender#9999"))
            .ReturnsAsync("Sender#9999");

        var hub = new WebsiteBackendHub(
            authService.Object,
            connections,
            contextAccessor.Object,
            friendRequestCacheMock.Object,
            personalSettingsRepo.Object,
            friendCommandHandlerMock.Object,
            tracingService,
            _battleTagResolverMock.Object
        );
        typeof(Hub).GetProperty("Clients").SetValue(hub, mockClients.Object);
        SetHubContext(hub, "connection-id-1");
        connections.Add("connection-id-1", new WebSocketUser { BattleTag = "JwtUser#1234", ConnectionId = "connection-id-1" });

        var req = new FriendRequest("Sender#9999", "Impersonated#5678"); // forged Receiver

        // Act
        await hub.BlockIncomingFriendRequest(req);

        // Assert: LoadFriendRequest was called with the JWT-pinned Receiver
        friendRequestCacheMock.Verify(
            c => c.LoadFriendRequest(It.Is<FriendRequest>(r => r.Sender == "Sender#9999" && r.Receiver == "JwtUser#1234")),
            Times.Once,
            "LoadFriendRequest must be called with the JWT-pinned Receiver."
        );
    }

    [Test]
    public async Task DeleteOutgoingFriendRequest_OverwritesPayloadSenderWithJwtBattleTag()
    {
        // Arrange: attacker sends a request with forged Sender, JWT says "JwtUser#1234"
        var friendCommandHandlerMock = new Mock<IFriendCommandHandler>();
        friendCommandHandlerMock
            .Setup(h => h.LoadFriendList(It.IsAny<string>()))
            .ReturnsAsync((string bt) => new Friendlist(bt));
        friendCommandHandlerMock
            .Setup(h => h.DeleteFriendRequest(It.IsAny<FriendRequest>()))
            .Returns(Task.CompletedTask);

        var friendRequestCacheMock = new Mock<IFriendRequestCache>();
        var storedReq = new FriendRequest("JwtUser#1234", "Receiver#9999");
        friendRequestCacheMock
            .Setup(c => c.LoadSentFriendRequests(It.IsAny<string>()))
            .ReturnsAsync(new System.Collections.Generic.List<FriendRequest>());
        friendRequestCacheMock
            .Setup(c => c.LoadReceivedFriendRequests(It.IsAny<string>()))
            .ReturnsAsync(new System.Collections.Generic.List<FriendRequest>());
        friendRequestCacheMock
            .Setup(c => c.LoadFriendRequest(It.IsAny<FriendRequest>()))
            .ReturnsAsync((FriendRequest r) =>
                r.Sender == "JwtUser#1234" && r.Receiver == "Receiver#9999" ? storedReq : null);

        _battleTagResolverMock
            .Setup(r => r.ResolveCanonical("Receiver#9999"))
            .ReturnsAsync("Receiver#9999");

        var hub = new WebsiteBackendHub(
            authService.Object,
            connections,
            contextAccessor.Object,
            friendRequestCacheMock.Object,
            personalSettingsRepo.Object,
            friendCommandHandlerMock.Object,
            tracingService,
            _battleTagResolverMock.Object
        );
        typeof(Hub).GetProperty("Clients").SetValue(hub, mockClients.Object);
        SetHubContext(hub, "connection-id-1");
        connections.Add("connection-id-1", new WebSocketUser { BattleTag = "JwtUser#1234", ConnectionId = "connection-id-1" });

        var req = new FriendRequest("Impersonated#5678", "Receiver#9999"); // forged Sender

        // Act
        await hub.DeleteOutgoingFriendRequest(req);

        // Assert: LoadFriendRequest was called with the JWT-pinned Sender
        friendRequestCacheMock.Verify(
            c => c.LoadFriendRequest(It.Is<FriendRequest>(r => r.Sender == "JwtUser#1234" && r.Receiver == "Receiver#9999")),
            Times.Once,
            "LoadFriendRequest must be called with the JWT-pinned Sender."
        );
    }

    [Test]
    public async Task MakeFriendRequest_CanonicalReceiver_ProceedsToCreate()
    {
        // Arrange: client sends a properly-canonical Receiver
        var friendCommandHandlerMock = new Mock<IFriendCommandHandler>();
        friendCommandHandlerMock
            .Setup(h => h.LoadFriendList(It.IsAny<string>()))
            .ReturnsAsync((string bt) => new Friendlist(bt));
        friendCommandHandlerMock
            .Setup(h => h.CreateFriendRequest(It.IsAny<FriendRequest>()))
            .Returns(Task.CompletedTask);

        personalSettingsRepo
            .Setup(r => r.Find(It.IsAny<string>()))
            .ReturnsAsync(new PersonalSetting("TORREN#11438"));

        _battleTagResolverMock
            .Setup(r => r.ResolveCanonical("TORREN#11438"))
            .ReturnsAsync("TORREN#11438"); // canonical matches input — passes through

        var hub = CreateHub(friendCommandHandlerMock.Object);
        SetHubContext(hub, "connection-id-1");
        connections.Add("connection-id-1", new WebSocketUser { BattleTag = "Sender#1234", ConnectionId = "connection-id-1" });

        var req = new FriendRequest("Sender#1234", "TORREN#11438");

        // Act
        await hub.MakeFriendRequest(req);

        // Assert: CreateFriendRequest was called (flow proceeded)
        friendCommandHandlerMock.Verify(h => h.CreateFriendRequest(It.IsAny<FriendRequest>()), Times.Once);
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
