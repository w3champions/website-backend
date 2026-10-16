using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http;
using Moq;
using W3ChampionsStatisticService.Hubs;
using W3ChampionsStatisticService.NetworkDiagnostics.Delivery;
using W3ChampionsStatisticService.Friends;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.Filters;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.Sessions;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using NUnit.Framework;
using W3C.Domain.ChatService;

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
    private ConnectionMapping connections;
    private Mock<IHttpContextAccessor> contextAccessor;
    private FakeFriendRequestCache friendRequestCache;
    private Mock<IPersonalSettingsRepository> personalSettingsRepo;
    private TestFriendListCache friendListCache;
    private TestFriendRepository friendRepository;
    private TracingService tracingService;
    private Mock<INetworkDiagnosticsDelivery> netDiagDelivery;
    private Mock<IHubCallerClients> mockClients;
    private Mock<ISingleClientProxy> mockCaller;
    private Mock<IClientProxy> mockFriendsProxy;
    private Mock<IBattleTagResolver> _battleTagResolverMock;
    private Mock<IRelationshipChangeNotifier> relationshipNotifier;
    private TicketStore ticketStore;
    private Mock<IAdminRoster> adminRoster;

    [SetUp]
    public void SetUp()
    {
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
        mockFriendsProxy = new Mock<IClientProxy>();
        mockClients.Setup(clients => clients.Clients(It.IsAny<IReadOnlyList<string>>())).Returns(mockFriendsProxy.Object);
        _battleTagResolverMock = new Mock<IBattleTagResolver>();
        // Default: every BattleTag resolves as canonical (returns input unchanged).
        // Individual tests override this for non-canonical or not-found scenarios.
        _battleTagResolverMock
            .Setup(r => r.ResolveCanonical(It.IsAny<string>()))
            .ReturnsAsync((string input) => input);
        relationshipNotifier = new Mock<IRelationshipChangeNotifier>();
        ticketStore = new TicketStore();
        netDiagDelivery = new Mock<INetworkDiagnosticsDelivery>();
        adminRoster = new Mock<IAdminRoster>();
        SetAdmins();
        adminRoster.Setup(r => r.IsAdmin(It.IsAny<string>())).ReturnsAsync(false);
    }

    private void SetAdmins(params string[] admins)
    {
        adminRoster
            .Setup(r => r.GetAdminBattleTags())
            .ReturnsAsync(new HashSet<string>(admins, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Captures the Friendlist (args[0]) of every FriendResponseData sent through the given proxy.</summary>
    private static List<Friendlist> CaptureFriendResponseData<TProxy>(Mock<TProxy> proxy) where TProxy : class, IClientProxy
    {
        var captured = new List<Friendlist>();
        proxy
            .Setup(c => c.SendCoreAsync("FriendResponseData", It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Callback((string _, object[] args, CancellationToken _) => captured.Add((Friendlist)args[0]))
            .Returns(Task.CompletedTask);
        return captured;
    }

    private WebsiteBackendHub CreateHub(IFriendCommandHandler friendCommandHandler)
    {
        var hub = new WebsiteBackendHub(
            connections,
            contextAccessor.Object,
            friendRequestCache,
            personalSettingsRepo.Object,
            friendCommandHandler,
            tracingService,
            _battleTagResolverMock.Object,
            relationshipNotifier.Object,
            ticketStore,
            netDiagDelivery.Object,
            adminRoster.Object
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
            connections,
            contextAccessor.Object,
            friendRequestCacheMock.Object,
            personalSettingsRepo.Object,
            friendCommandHandlerMock.Object,
            tracingService,
            _battleTagResolverMock.Object,
            relationshipNotifier.Object,
            ticketStore,
            netDiagDelivery.Object,
            adminRoster.Object
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
            connections,
            contextAccessor.Object,
            friendRequestCacheMock.Object,
            personalSettingsRepo.Object,
            friendCommandHandlerMock.Object,
            tracingService,
            _battleTagResolverMock.Object,
            relationshipNotifier.Object,
            ticketStore,
            netDiagDelivery.Object,
            adminRoster.Object
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
            connections,
            contextAccessor.Object,
            friendRequestCacheMock.Object,
            personalSettingsRepo.Object,
            friendCommandHandlerMock.Object,
            tracingService,
            _battleTagResolverMock.Object,
            relationshipNotifier.Object,
            ticketStore,
            netDiagDelivery.Object,
            adminRoster.Object
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
            connections,
            contextAccessor.Object,
            friendRequestCacheMock.Object,
            personalSettingsRepo.Object,
            friendCommandHandlerMock.Object,
            tracingService,
            _battleTagResolverMock.Object,
            relationshipNotifier.Object,
            ticketStore,
            netDiagDelivery.Object,
            adminRoster.Object
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

    [Test]
    public async Task OnDisconnectedAsync_NeverEmitsFriendOnlineStatus()
    {
        // Arrange: "User#1234" is connected and has "Friend#5678" (also connected) as a friend.
        // Presence is now owned entirely by chat-service's FriendPresenceChanged; the hub must
        // never broadcast the legacy "FriendOnlineStatus" event, on any proxy.
        var friendList = new Friendlist("User#1234");
        friendList.Friends.Add("Friend#5678");
        friendListCache.Upsert(friendList);
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        var hub = CreateHub(friendCommandHandler);

        connections.Add("conn1", new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" });
        connections.Add("connFriend", new WebSocketUser { BattleTag = "Friend#5678", ConnectionId = "connFriend" });

        SetHubContext(hub, "conn1");

        // Act
        await hub.OnDisconnectedAsync(null);

        // Assert: no FriendOnlineStatus emission anywhere, but connection-mapping bookkeeping
        // still happened (ConnectionMapping still drives friends-CRUD push).
        mockFriendsProxy.Verify(
            p => p.SendCoreAsync("FriendOnlineStatus", It.IsAny<object[]>(), default),
            Times.Never
        );
        mockCaller.Verify(
            c => c.SendCoreAsync("FriendOnlineStatus", It.IsAny<object[]>(), default),
            Times.Never
        );
        Assert.That(connections.GetUser("conn1"), Is.Null);
    }

    [Test]
    public async Task OnConnectedAsync_NeverEmitsFriendOnlineStatus()
    {
        // Arrange: "Friend#5678" is already online; "User#1234" (its friend) connects. The
        // handshake ("Connected") must still fire, but the legacy presence broadcast must not.
        var friendList = new Friendlist("User#1234");
        friendList.Friends.Add("Friend#5678");
        friendListCache.Upsert(friendList);
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        var hub = CreateHub(friendCommandHandler);

        connections.Add("connFriend", new WebSocketUser { BattleTag = "Friend#5678", ConnectionId = "connFriend" });

        var ticket = ticketStore.Mint(new W3CUserAuthenticationDto { BattleTag = "User#1234" }, DateTime.UtcNow);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString($"?access_token={ticket}");
        contextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        SetHubContext(hub, "conn1");

        // Act
        await hub.OnConnectedAsync();

        // Assert
        mockCaller.Verify(c => c.SendCoreAsync("Connected", It.IsAny<object[]>(), default), Times.Once);
        mockFriendsProxy.Verify(
            p => p.SendCoreAsync("FriendOnlineStatus", It.IsAny<object[]>(), default),
            Times.Never
        );
        mockCaller.Verify(
            c => c.SendCoreAsync("FriendOnlineStatus", It.IsAny<object[]>(), default),
            Times.Never
        );
    }

    // --- Raw JWT (released launchers predating the ticket flow) is rejected -------------------

    [Test]
    public async Task OnConnectedAsync_RawJwt_Rejected_NoConnect()
    {
        // A pre-ticket launcher sends its raw W3C JWT as access_token. The hub is ticket-only:
        // AuthorizationFailed + abort, never registered.
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);
        var hub = CreateHub(friendCommandHandler);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?access_token=eyJhbGciOiJSUzI1NiJ9.eyJidCI6IkxlZ2FjeSMxMjM0In0.c2ln");
        contextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        SetHubContext(hub, "conn-legacy");

        await hub.OnConnectedAsync();

        Assert.That(connections.GetUser("conn-legacy"), Is.Null);
        mockCaller.Verify(c => c.SendCoreAsync("AuthorizationFailed", It.IsAny<object[]>(), default), Times.Once);
        mockCaller.Verify(c => c.SendCoreAsync("Connected", It.IsAny<object[]>(), default), Times.Never);
    }

    [Test]
    public void OnDisconnectedAsync_UnknownConnection_DoesNotThrow()
    {
        // Arrange: connection never completed authentication, so `connections.GetUser(...)` is null.
        // Pins the boyscout NRE fix: `user.BattleTag` used to be dereferenced outside the `user != null`
        // guard, so this used to throw a NullReferenceException.
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);
        var hub = CreateHub(friendCommandHandler);

        SetHubContext(hub, "never-authenticated-conn");

        // Act + Assert
        Assert.DoesNotThrowAsync(async () => await hub.OnDisconnectedAsync(null));
    }

    // --- Relationship change-ping wiring (Task 3) ---------------------------------------------

    [Test]
    public async Task AcceptIncomingFriendRequest_FiresExactlyOneFriendAddPing()
    {
        var friendList = new Friendlist("Receiver#1");
        friendListCache.Upsert(friendList);
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        var req = new FriendRequest("Sender#1", "Receiver#1");
        friendRequestCache.AddRequest(req);
        friendRequestCache.AddRequest(new FriendRequest("Receiver#1", "Sender#1"));

        var hub = CreateHub(friendCommandHandler);
        connections.Add("conn1", new WebSocketUser { BattleTag = "Receiver#1", ConnectionId = "conn1" });
        SetHubContext(hub, "conn1");

        await hub.AcceptIncomingFriendRequest(req);

        relationshipNotifier.Verify(
            n => n.NotifyChange(RelationshipChangeType.FriendAdd, "Receiver#1", "Sender#1"),
            Times.Once
        );
        // No double-ping from the mutual add (only one ping call total, for ANY args).
        relationshipNotifier.Verify(
            n => n.NotifyChange(It.IsAny<RelationshipChangeType>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Once
        );
    }

    [Test]
    public async Task RemoveFriend_FiresExactlyOneFriendRemovePing()
    {
        var friendList = new Friendlist("User#1234");
        friendList.Friends.Add("Friend#5678");
        friendListCache.Upsert(friendList);
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        var hub = CreateHub(friendCommandHandler);
        connections.Add("conn1", new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" });
        SetHubContext(hub, "conn1");

        await hub.RemoveFriend("Friend#5678");

        relationshipNotifier.Verify(
            n => n.NotifyChange(RelationshipChangeType.FriendRemove, "User#1234", "Friend#5678"),
            Times.Once
        );
    }

    [Test]
    public async Task BlockPlayer_FiresBlockPing()
    {
        var friendList = new Friendlist("User#1234");
        friendListCache.Upsert(friendList);
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        var hub = CreateHub(friendCommandHandler);
        connections.Add("conn1", new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" });
        SetHubContext(hub, "conn1");

        await hub.BlockPlayer("Blocked#5678");

        relationshipNotifier.Verify(
            n => n.NotifyChange(RelationshipChangeType.Block, "User#1234", "Blocked#5678"),
            Times.Once
        );
    }

    [Test]
    public async Task BlockIncomingFriendRequest_FiresBlockPing()
    {
        var friendList = new Friendlist("Receiver#1");
        friendListCache.Upsert(friendList);
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        var req = new FriendRequest("Sender#1", "Receiver#1");
        friendRequestCache.AddRequest(req);

        var hub = CreateHub(friendCommandHandler);
        connections.Add("conn1", new WebSocketUser { BattleTag = "Receiver#1", ConnectionId = "conn1" });
        SetHubContext(hub, "conn1");

        await hub.BlockIncomingFriendRequest(req);

        relationshipNotifier.Verify(
            n => n.NotifyChange(RelationshipChangeType.Block, "Receiver#1", "Sender#1"),
            Times.Once
        );
    }

    [Test]
    public async Task UnblockFriendRequestsFromPlayer_FiresUnblockPing()
    {
        var friendList = new Friendlist("User#1234");
        friendList.BlockedBattleTags.Add("Blocked#5678");
        friendListCache.Upsert(friendList);
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        var hub = CreateHub(friendCommandHandler);
        connections.Add("conn1", new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" });
        SetHubContext(hub, "conn1");

        await hub.UnblockFriendRequestsFromPlayer("Blocked#5678");

        relationshipNotifier.Verify(
            n => n.NotifyChange(RelationshipChangeType.Unblock, "User#1234", "Blocked#5678"),
            Times.Once
        );
    }

    [TestCase("MakeFriendRequest", "Sender#1")]
    [TestCase("DenyIncomingFriendRequest", "Receiver#1")]
    [TestCase("DeleteOutgoingFriendRequest", "Sender#1")]
    public async Task MakeFriendRequest_DenyIncoming_DeleteOutgoing_NeverPing(string methodName, string userBattleTag)
    {
        personalSettingsRepo.Setup(r => r.Find(It.IsAny<string>())).ReturnsAsync(new PersonalSetting("Receiver#1"));

        var friendList = new Friendlist(userBattleTag);
        friendListCache.Upsert(friendList);
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        var req = new FriendRequest("Sender#1", "Receiver#1");
        if (methodName != "MakeFriendRequest")
        {
            friendRequestCache.AddRequest(req);
        }

        var hub = CreateHub(friendCommandHandler);
        connections.Add("conn1", new WebSocketUser { BattleTag = userBattleTag, ConnectionId = "conn1" });
        SetHubContext(hub, "conn1");

        var method = typeof(WebsiteBackendHub).GetMethod(methodName);
        await (Task)method.Invoke(hub, new object[] { req });

        relationshipNotifier.Verify(
            n => n.NotifyChange(It.IsAny<RelationshipChangeType>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never
        );
    }

    [Test]
    public async Task BlockPlayer_AlreadyBlocked_NoPing()
    {
        var friendList = new Friendlist("User#1234");
        friendList.BlockedBattleTags.Add("Blocked#5678");
        friendListCache.Upsert(friendList);
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        var hub = CreateHub(friendCommandHandler);
        connections.Add("conn1", new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" });
        SetHubContext(hub, "conn1");

        // CanBlock throws (already blocked) before any persistence happens.
        await hub.BlockPlayer("Blocked#5678");

        relationshipNotifier.Verify(
            n => n.NotifyChange(It.IsAny<RelationshipChangeType>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never
        );
        mockCaller.Verify(
            c => c.SendCoreAsync(
                It.Is<string>(s => s.Contains("FriendResponseMessage")),
                It.IsAny<object[]>(),
                default
            ),
            Times.Once
        );
    }

    [Test]
    public async Task BlockPlayer_PersistenceFails_NoPing()
    {
        var friendCommandHandlerMock = new Mock<IFriendCommandHandler>();
        friendCommandHandlerMock
            .Setup(h => h.LoadFriendList(It.IsAny<string>()))
            .ReturnsAsync((string bt) => new Friendlist(bt));
        friendCommandHandlerMock
            .Setup(h => h.UpsertFriendList(It.IsAny<Friendlist>()))
            .ThrowsAsync(new System.Exception("db down"));

        var hub = CreateHub(friendCommandHandlerMock.Object);
        connections.Add("conn1", new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" });
        SetHubContext(hub, "conn1");

        await hub.BlockPlayer("Blocked#5678");

        relationshipNotifier.Verify(
            n => n.NotifyChange(It.IsAny<RelationshipChangeType>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never
        );
        mockCaller.Verify(
            c => c.SendCoreAsync(
                It.Is<string>(s => s.Contains("FriendResponseMessage")),
                It.IsAny<object[]>(),
                default
            ),
            Times.Once
        );
    }

    [Test]
    public async Task AcceptIncomingFriendRequest_PingFiresAfterBothUpserts()
    {
        var order = new List<string>();

        var friendCommandHandlerMock = new Mock<IFriendCommandHandler>();
        friendCommandHandlerMock
            .Setup(h => h.LoadFriendList(It.IsAny<string>()))
            .ReturnsAsync((string bt) => new Friendlist(bt));
        friendCommandHandlerMock
            .Setup(h => h.DeleteFriendRequest(It.IsAny<FriendRequest>()))
            .Returns(Task.CompletedTask);
        friendCommandHandlerMock
            .Setup(h => h.AddFriend(It.IsAny<Friendlist>(), It.IsAny<string>()))
            .Returns<Friendlist, string>((fl, bt) =>
            {
                order.Add("persist");
                return Task.FromResult(fl);
            });

        var friendRequestCacheMock = new Mock<IFriendRequestCache>();
        var storedReq = new FriendRequest("Sender#1", "Receiver#1");
        var reciprocalReq = new FriendRequest("Receiver#1", "Sender#1");
        friendRequestCacheMock
            .Setup(c => c.LoadFriendRequest(It.Is<FriendRequest>(r => r.Sender == "Sender#1" && r.Receiver == "Receiver#1")))
            .ReturnsAsync(storedReq);
        friendRequestCacheMock
            .Setup(c => c.LoadFriendRequest(It.Is<FriendRequest>(r => r.Sender == "Receiver#1" && r.Receiver == "Sender#1")))
            .ReturnsAsync(reciprocalReq);
        friendRequestCacheMock
            .Setup(c => c.LoadSentFriendRequests(It.IsAny<string>()))
            .ReturnsAsync(new List<FriendRequest>());
        friendRequestCacheMock
            .Setup(c => c.LoadReceivedFriendRequests(It.IsAny<string>()))
            .ReturnsAsync(new List<FriendRequest>());

        relationshipNotifier
            .Setup(n => n.NotifyChange(It.IsAny<RelationshipChangeType>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => order.Add("ping"));

        var hub = new WebsiteBackendHub(
            connections,
            contextAccessor.Object,
            friendRequestCacheMock.Object,
            personalSettingsRepo.Object,
            friendCommandHandlerMock.Object,
            tracingService,
            _battleTagResolverMock.Object,
            relationshipNotifier.Object,
            ticketStore,
            netDiagDelivery.Object,
            adminRoster.Object
        );
        typeof(Hub).GetProperty("Clients").SetValue(hub, mockClients.Object);
        connections.Add("conn1", new WebSocketUser { BattleTag = "Receiver#1", ConnectionId = "conn1" });
        SetHubContext(hub, "conn1");

        var req = new FriendRequest("Sender#1", "Receiver#1");

        await hub.AcceptIncomingFriendRequest(req);

        Assert.That(order, Is.EqualTo(new[] { "persist", "persist", "ping" }));
    }

    [Test]
    public void BlockPlayer_ThrowingNotifier_UserVisibleBehaviorUnchanged()
    {
        var friendList = new Friendlist("User#1234");
        friendListCache.Upsert(friendList);
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        relationshipNotifier
            .Setup(n => n.NotifyChange(It.IsAny<RelationshipChangeType>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new System.Exception("notifier boom"));

        var hub = CreateHub(friendCommandHandler);
        connections.Add("conn1", new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" });
        SetHubContext(hub, "conn1");

        Assert.DoesNotThrowAsync(async () => await hub.BlockPlayer("Blocked#5678"));

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

    // --- Admin roster: outbound block lists never carry admins (storage untouched) --------------

    [Test]
    public async Task LoadFriendListAndRequests_SendsBlockedListWithoutAdmins_StorageUntouched()
    {
        var friendList = new Friendlist("User#1234");
        friendList.BlockedBattleTags.AddRange(new[] { "Admin#2", "Foe#3" });
        friendListCache.Upsert(friendList);
        SetAdmins("Admin#2");
        var sent = CaptureFriendResponseData(mockCaller);
        var hub = CreateHub(new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache));
        connections.Add("conn1", new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" });
        SetHubContext(hub, "conn1");

        await hub.LoadFriendListAndRequestsTraced(new SignalRTraceContextFilter.PreventZeroArgumentHandler());

        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0].BlockedBattleTags, Is.EqualTo(new List<string> { "Foe#3" }));
        var cached = await friendListCache.LoadFriendList("User#1234");
        Assert.That(cached.BlockedBattleTags, Is.EqualTo(new List<string> { "Admin#2", "Foe#3" }));
    }

    [Test]
    public async Task RemoveFriend_PushToOtherPlayer_SendsBlockedListWithoutAdmins()
    {
        var friendList = new Friendlist("User#1234");
        friendList.Friends.Add("Friend#5678");
        friendListCache.Upsert(friendList);
        var otherList = new Friendlist("Friend#5678");
        otherList.Friends.Add("User#1234");
        otherList.BlockedBattleTags.Add("Admin#2");
        friendListCache.Upsert(otherList);
        SetAdmins("Admin#2");
        var otherProxy = new Mock<ISingleClientProxy>();
        mockClients.Setup(clients => clients.Client("conn2")).Returns(otherProxy.Object);
        var pushed = CaptureFriendResponseData(otherProxy);
        var hub = CreateHub(new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache));
        connections.Add("conn1", new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" });
        connections.Add("conn2", new WebSocketUser { BattleTag = "Friend#5678", ConnectionId = "conn2" });
        SetHubContext(hub, "conn1");

        await hub.RemoveFriend("Friend#5678");

        Assert.That(pushed, Has.Count.EqualTo(1));
        Assert.That(pushed[0].BlockedBattleTags, Is.Empty);
        Assert.That(otherList.BlockedBattleTags, Is.EqualTo(new List<string> { "Admin#2" }));
    }

    [Test]
    public async Task UnblockFriendRequestsFromPlayer_ResponseOmitsRemainingBlockedAdmins()
    {
        var friendList = new Friendlist("User#1234");
        friendList.BlockedBattleTags.AddRange(new[] { "Admin#2", "Foe#3" });
        friendListCache.Upsert(friendList);
        SetAdmins("Admin#2");
        var sent = CaptureFriendResponseData(mockCaller);
        var hub = CreateHub(new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache));
        connections.Add("conn1", new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" });
        SetHubContext(hub, "conn1");

        await hub.UnblockFriendRequestsFromPlayer("Foe#3");

        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0].BlockedBattleTags, Is.Empty);
        var cached = await friendListCache.LoadFriendList("User#1234");
        Assert.That(cached.BlockedBattleTags, Is.EqualTo(new List<string> { "Admin#2" }));
    }

    // --- Admin roster: admins can't be blocked; BlockPlayer reports whether it blocked ---------

    private const string AdminTag = "Admin#1";

    private void MarkAdmin(string battleTag)
    {
        SetAdmins(battleTag);
        adminRoster.Setup(r => r.IsAdmin(battleTag)).ReturnsAsync(true);
    }

    private WebsiteBackendHub CreateConnectedHub(string battleTag)
    {
        var hub = CreateHub(new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache));
        connections.Add("conn1", new WebSocketUser { BattleTag = battleTag, ConnectionId = "conn1" });
        SetHubContext(hub, "conn1");
        return hub;
    }

    private static object ReadAnonymousProperty(object source, string name) =>
        source.GetType().GetProperty(name)?.GetValue(source);

    [Test]
    public async Task BlockPlayer_TargetIsAdmin_RefusesAndDoesNotPersist()
    {
        MarkAdmin(AdminTag);
        var friendList = new Friendlist("User#1234");
        friendListCache.Upsert(friendList);
        var hub = CreateConnectedHub("User#1234");

        var result = await hub.BlockPlayer(AdminTag);

        Assert.IsFalse(result);
        Assert.That(friendList.BlockedBattleTags, Does.Not.Contain(AdminTag));
        mockCaller.Verify(c => c.SendCoreAsync("FriendResponseMessage",
            It.Is<object[]>(a => a.Length == 1 && (string)a[0] == "Admins can't be blocked."), default), Times.Once);
        mockCaller.Verify(c => c.SendCoreAsync("FriendResponseData", It.IsAny<object[]>(), default), Times.Never);
        relationshipNotifier.Verify(n => n.NotifyChange(It.IsAny<RelationshipChangeType>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.That(WebsiteBackendHub.AdminsCantBeBlockedMessage, Is.EqualTo("Admins can't be blocked."));
    }

    [Test]
    public async Task BlockPlayer_AdminAlreadyBlockedInStorage_StillGetsAdminRefusal()
    {
        MarkAdmin(AdminTag);
        var friendList = new Friendlist("User#1234");
        friendList.BlockedBattleTags.Add(AdminTag);
        friendListCache.Upsert(friendList);
        var hub = CreateConnectedHub("User#1234");

        var result = await hub.BlockPlayer(AdminTag);

        Assert.IsFalse(result);
        mockCaller.Verify(c => c.SendCoreAsync("FriendResponseMessage",
            It.Is<object[]>(a => a.Length == 1 && (string)a[0] == "Admins can't be blocked."), default), Times.Once);
        mockCaller.Verify(c => c.SendCoreAsync("FriendResponseMessage", It.IsAny<object[]>(), default), Times.Once);
        Assert.That(friendList.BlockedBattleTags, Is.EqualTo(new List<string> { AdminTag }));
    }

    [Test]
    public async Task BlockPlayer_AdminRefusal_MessageSentBeforeResultCompletes()
    {
        MarkAdmin(AdminTag);
        friendListCache.Upsert(new Friendlist("User#1234"));
        var sendGate = new TaskCompletionSource();
        mockCaller
            .Setup(c => c.SendCoreAsync("FriendResponseMessage", It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(sendGate.Task);
        var hub = CreateConnectedHub("User#1234");

        var pending = hub.BlockPlayer(AdminTag);

        // The launcher reads friendResponseMessage as soon as the invoke resolves, so the message must
        // be sent (and awaited) before the result is produced.
        mockCaller.Verify(c => c.SendCoreAsync("FriendResponseMessage",
            It.Is<object[]>(a => (string)a[0] == "Admins can't be blocked."), It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(pending.IsCompleted, Is.False);
        sendGate.SetResult();
        Assert.IsFalse(await pending);
    }

    [Test]
    public async Task BlockPlayer_ResolutionError_SentBeforeResultCompletes()
    {
        _battleTagResolverMock.Setup(r => r.ResolveCanonical("Ghost#1")).ReturnsAsync((string)null);
        var sendGate = new TaskCompletionSource();
        mockCaller
            .Setup(c => c.SendCoreAsync("BattleTagResolutionError", It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(sendGate.Task);
        var hub = CreateConnectedHub("User#1234");

        var pending = hub.BlockPlayer("Ghost#1");

        mockCaller.Verify(c => c.SendCoreAsync("BattleTagResolutionError", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(pending.IsCompleted, Is.False);
        sendGate.SetResult();
        Assert.IsFalse(await pending);
    }

    [Test]
    public async Task BlockPlayer_NonCanonicalAdminTag_ReturnsResolutionErrorNotRefusal()
    {
        MarkAdmin(AdminTag);
        _battleTagResolverMock.Setup(r => r.ResolveCanonical("admin#1")).ReturnsAsync(AdminTag);
        var friendList = new Friendlist("User#1234");
        friendListCache.Upsert(friendList);
        var hub = CreateConnectedHub("User#1234");

        var result = await hub.BlockPlayer("admin#1");

        Assert.IsFalse(result);
        mockCaller.Verify(c => c.SendCoreAsync("BattleTagResolutionError",
            It.Is<object[]>(a => (string)ReadAnonymousProperty(a[0], "reason") == "non_canonical_battletag"), default), Times.Once);
        mockCaller.Verify(c => c.SendCoreAsync("FriendResponseMessage", It.IsAny<object[]>(), default), Times.Never);
        adminRoster.Verify(r => r.IsAdmin(It.IsAny<string>()), Times.Never);
        Assert.That(friendList.BlockedBattleTags, Is.Empty);
    }

    [Test]
    public async Task BlockPlayer_UnknownTag_EmitsResolutionErrorAndReturnsFalse()
    {
        _battleTagResolverMock.Setup(r => r.ResolveCanonical("Ghost#1")).ReturnsAsync((string)null);
        var friendList = new Friendlist("User#1234");
        friendListCache.Upsert(friendList);
        var hub = CreateConnectedHub("User#1234");

        var result = await hub.BlockPlayer("Ghost#1");

        Assert.IsFalse(result);
        Assert.That(friendList.BlockedBattleTags, Is.Empty);
        adminRoster.Verify(r => r.IsAdmin(It.IsAny<string>()), Times.Never);
        mockCaller.Verify(c => c.SendCoreAsync("FriendResponseMessage", It.IsAny<object[]>(), default), Times.Never);
        mockCaller.Verify(c => c.SendCoreAsync("BattleTagResolutionError",
            It.Is<object[]>(a => a.Length == 1
                && (string)ReadAnonymousProperty(a[0], "reason") == "user_not_found"
                && (string)ReadAnonymousProperty(a[0], "input") == "Ghost#1"), default), Times.Once);
    }

    [Test]
    public async Task BlockPlayer_NoConnectionUser_ReturnsFalse()
    {
        var hub = CreateHub(new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache));
        SetHubContext(hub, "unknown-conn");

        Assert.IsFalse(await hub.BlockPlayer("Blocked#5678"));
        adminRoster.Verify(r => r.IsAdmin(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task BlockPlayer_NonAdmin_ReturnsTrue()
    {
        var friendList = new Friendlist("User#1234");
        friendListCache.Upsert(friendList);
        var hub = CreateConnectedHub("User#1234");

        Assert.IsTrue(await hub.BlockPlayer("Blocked#5678"));
        Assert.That(friendList.BlockedBattleTags, Does.Contain("Blocked#5678"));
        mockCaller.Verify(c => c.SendCoreAsync("FriendResponseData",
            It.Is<object[]>(a => (string)a[3] == "Player Blocked#5678 blocked!"), default), Times.Once);
    }

    [Test]
    public async Task BlockPlayer_AlreadyBlocked_ReturnsFalse()
    {
        var friendList = new Friendlist("User#1234");
        friendList.BlockedBattleTags.Add("Blocked#5678");
        friendListCache.Upsert(friendList);
        var hub = CreateConnectedHub("User#1234");

        Assert.IsFalse(await hub.BlockPlayer("Blocked#5678"));
        mockCaller.Verify(c => c.SendCoreAsync("FriendResponseMessage",
            It.Is<object[]>(a => (string)a[0] == "You have already blocked this player."), default), Times.Once);
    }

    [Test]
    public async Task BlockPlayer_PersistenceFails_ReturnsFalse()
    {
        var friendCommandHandlerMock = new Mock<IFriendCommandHandler>();
        friendCommandHandlerMock
            .Setup(h => h.LoadFriendList(It.IsAny<string>()))
            .ReturnsAsync((string bt) => new Friendlist(bt));
        friendCommandHandlerMock
            .Setup(h => h.UpsertFriendList(It.IsAny<Friendlist>()))
            .ThrowsAsync(new Exception("db down"));
        var hub = CreateHub(friendCommandHandlerMock.Object);
        connections.Add("conn1", new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" });
        SetHubContext(hub, "conn1");

        Assert.IsFalse(await hub.BlockPlayer("Blocked#5678"));
    }

    [Test]
    public async Task BlockPlayer_PersistedButResponseSendFails_ReturnsTrue()
    {
        var friendList = new Friendlist("User#1234");
        friendListCache.Upsert(friendList);
        mockCaller
            .Setup(c => c.SendCoreAsync("FriendResponseData", It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("connection gone"));
        var hub = CreateConnectedHub("User#1234");

        Assert.IsTrue(await hub.BlockPlayer("Blocked#5678"));
        Assert.That(friendList.BlockedBattleTags, Does.Contain("Blocked#5678"));
    }

    [Test]
    public async Task BlockIncomingFriendRequest_FromAdmin_DeclinesRequestButDoesNotBlock()
    {
        MarkAdmin(AdminTag);
        var friendList = new Friendlist("User#1234");
        friendListCache.Upsert(friendList);
        var req = new FriendRequest(AdminTag, "User#1234");
        friendRequestCache.AddRequest(req);
        var adminProxy = new Mock<ISingleClientProxy>();
        mockClients.Setup(clients => clients.Client("conn2")).Returns(adminProxy.Object);
        connections.Add("conn2", new WebSocketUser { BattleTag = AdminTag, ConnectionId = "conn2" });
        var hub = CreateConnectedHub("User#1234");

        await hub.BlockIncomingFriendRequest(new FriendRequest(AdminTag, "User#1234"));

        Assert.IsFalse(await friendRequestCache.FriendRequestExists(new FriendRequest(AdminTag, "User#1234")));
        Assert.That(friendList.BlockedBattleTags, Does.Not.Contain(AdminTag));
        mockCaller.Verify(c => c.SendCoreAsync("FriendResponseData",
            It.Is<object[]>(a => (string)a[3] == "Admins can't be blocked."), default), Times.Once);
        mockCaller.Verify(c => c.SendCoreAsync("FriendResponseMessage", It.IsAny<object[]>(), default), Times.Never);
        relationshipNotifier.Verify(n => n.NotifyChange(It.IsAny<RelationshipChangeType>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        // The admin still gets the usual sentRequests push (now without the declined request).
        adminProxy.Verify(c => c.SendCoreAsync("FriendResponseData",
            It.Is<object[]>(a => a[0] == null && a[1] is List<FriendRequest> && ((List<FriendRequest>)a[1]).Count == 0), default), Times.Once);
    }

    [Test]
    public async Task BlockIncomingFriendRequest_FromAdmin_NoPendingRequest_ReportsNotFound()
    {
        MarkAdmin(AdminTag);
        var friendList = new Friendlist("User#1234");
        friendListCache.Upsert(friendList);
        var hub = CreateConnectedHub("User#1234");

        await hub.BlockIncomingFriendRequest(new FriendRequest(AdminTag, "User#1234"));

        mockCaller.Verify(c => c.SendCoreAsync("FriendResponseMessage",
            It.Is<object[]>(a => (string)a[0] == "Could not find a friend request to block."), default), Times.Once);
        Assert.That(friendList.BlockedBattleTags, Is.Empty);
    }

    [Test]
    public async Task MakeFriendRequest_FromAdminBlockedInStorage_StillRejected()
    {
        MarkAdmin(AdminTag);
        personalSettingsRepo.Setup(r => r.Find("User#1")).ReturnsAsync(new PersonalSetting("User#1"));
        var receiverList = new Friendlist("User#1");
        receiverList.BlockedBattleTags.Add(AdminTag);
        friendListCache.Upsert(receiverList);
        var hub = CreateConnectedHub(AdminTag);

        await hub.MakeFriendRequest(new FriendRequest(AdminTag, "User#1"));

        mockCaller.Verify(c => c.SendCoreAsync("FriendResponseMessage",
            It.Is<object[]>(a => (string)a[0] == "This player is not accepting friend requests."), default), Times.Once);
        Assert.IsFalse(await friendRequestCache.FriendRequestExists(new FriendRequest(AdminTag, "User#1")));
    }

    // --- Ticket-only auth on OnConnectedAsync (WB-1: hard cutover, no raw-JWT fallback) ----------
    // The launcher is this hub's SOLE client; browsers use REST + Bearer and never open this
    // WebSocket. A raw JWT (or any non-ticket string) MUST be rejected — there is no fallback.

    [Test]
    public async Task OnConnectedAsync_ValidTicket_ConnectsWithTicketIdentity()
    {
        // Arrange: a one-time ticket is minted into the SHARED store; the hub must consume it and
        // adopt the ticket's identity. There is no JWT path at all — the hub is ticket-only.
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);
        var hub = CreateHub(friendCommandHandler);

        var ticket = ticketStore.Mint(new W3CUserAuthenticationDto { BattleTag = "Ticket#1" }, DateTime.UtcNow);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString($"?access_token={ticket}");
        contextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        SetHubContext(hub, "conn1");

        // Act
        await hub.OnConnectedAsync();

        // Assert: connected as the TICKET identity.
        mockCaller.Verify(c => c.SendCoreAsync("Connected", It.IsAny<object[]>(), default), Times.Once);
        Assert.That(connections.GetUser("conn1")?.BattleTag, Is.EqualTo("Ticket#1"));
        // conn1 now holds the ticket, so no other connection can redeem it.
        Assert.IsFalse(ticketStore.TryRedeem(ticket, "conn-other", DateTime.UtcNow, out _));
    }

    [Test]
    public async Task OnConnectedAsync_CallsNetDiagDeliveryWithTicketIdentity()
    {
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);
        var hub = CreateHub(friendCommandHandler);
        var ticket = ticketStore.Mint(new W3CUserAuthenticationDto { BattleTag = "Ticket#1" }, DateTime.UtcNow);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString($"?access_token={ticket}");
        contextAccessor.Setup(a => a.HttpContext).Returns(httpContext);
        SetHubContext(hub, "conn1");

        await hub.OnConnectedAsync();

        netDiagDelivery.Verify(d => d.OnUserConnectedAsync("Ticket#1"), Times.Once);
    }

    [Test]
    public void OnConnectedAsync_NetDiagDeliveryThrows_StillConnects()
    {
        // Mongo down during the diagnostics on-connect path must not cost the user the hub connection.
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);
        var hub = CreateHub(friendCommandHandler);
        netDiagDelivery.Setup(d => d.OnUserConnectedAsync(It.IsAny<string>())).ThrowsAsync(new TimeoutException("mongo down"));
        var ticket = ticketStore.Mint(new W3CUserAuthenticationDto { BattleTag = "Ticket#1" }, DateTime.UtcNow);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString($"?access_token={ticket}");
        contextAccessor.Setup(a => a.HttpContext).Returns(httpContext);
        SetHubContext(hub, "conn1");

        Assert.DoesNotThrowAsync(() => hub.OnConnectedAsync());

        mockCaller.Verify(c => c.SendCoreAsync("Connected", It.IsAny<object[]>(), default), Times.Once);
        Assert.That(connections.GetUser("conn1")?.BattleTag, Is.EqualTo("Ticket#1"));
    }

    [Test]
    public void OnConnectedAsync_NetDiagDeliveryThrowsSynchronously_StillConnects()
    {
        // A delivery that throws before returning a task is contained the same way.
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);
        var hub = CreateHub(friendCommandHandler);
        netDiagDelivery.Setup(d => d.OnUserConnectedAsync(It.IsAny<string>())).Throws(new InvalidOperationException("boom"));
        var ticket = ticketStore.Mint(new W3CUserAuthenticationDto { BattleTag = "Ticket#1" }, DateTime.UtcNow);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString($"?access_token={ticket}");
        contextAccessor.Setup(a => a.HttpContext).Returns(httpContext);
        SetHubContext(hub, "conn1");

        Assert.DoesNotThrowAsync(() => hub.OnConnectedAsync());

        mockCaller.Verify(c => c.SendCoreAsync("Connected", It.IsAny<object[]>(), default), Times.Once);
    }

    // Fix round 1: the try/catch contains exceptions, the bound contains hangs (a stalled Mongo socket). SignalR
    // dispatches a connection's invocations only after OnConnectedAsync returns.
    [Test]
    public async Task OnConnectedAsync_NetDiagDeliveryNeverCompletes_StillReturnsWithinTheBound()
    {
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);
        var hub = CreateHub(friendCommandHandler);
        hub.NetDiagOnConnectBound = TimeSpan.FromMilliseconds(20);
        var neverCompletes = new TaskCompletionSource();
        netDiagDelivery.Setup(d => d.OnUserConnectedAsync(It.IsAny<string>())).Returns(neverCompletes.Task);
        var ticket = ticketStore.Mint(new W3CUserAuthenticationDto { BattleTag = "Ticket#1" }, DateTime.UtcNow);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString($"?access_token={ticket}");
        contextAccessor.Setup(a => a.HttpContext).Returns(httpContext);
        SetHubContext(hub, "conn1");
        var sink = new CollectingSink();
        var previousLogger = Serilog.Log.Logger;
        Serilog.Log.Logger = new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        try
        {
            // The outer WaitAsync only turns a regression (an unbounded await) into a failure instead of a hung run.
            await hub.OnConnectedAsync().WaitAsync(TimeSpan.FromSeconds(10));

            mockCaller.Verify(c => c.SendCoreAsync("Connected", It.IsAny<object[]>(), default), Times.Once);
            Assert.That(connections.GetUser("conn1")?.BattleTag, Is.EqualTo("Ticket#1"));
            Assert.AreEqual(1, sink.Events.Count, "one line for the timeout");
            Assert.That(sink.Events[0].Properties.Keys, Is.EquivalentTo(new[] { "BattleTag" }), "the timeout line names only the BattleTag");

            // The abandoned work fails later: that failure is observed and logged, not left as an unobserved task exception.
            neverCompletes.SetException(new InvalidOperationException("late failure"));

            Assert.AreEqual(2, sink.Events.Count);
            Assert.IsInstanceOf<InvalidOperationException>(sink.Events[1].Exception);
            Assert.That(sink.Events[1].Properties.Keys, Is.EquivalentTo(new[] { "BattleTag" }));
        }
        finally
        {
            Serilog.Log.Logger = previousLogger;
        }
    }

    [Test]
    public void NetDiagOnConnectBound_DefaultsToFiveSeconds()
    {
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        Assert.AreEqual(TimeSpan.FromSeconds(5), CreateHub(friendCommandHandler).NetDiagOnConnectBound);
    }

    private sealed class CollectingSink : Serilog.Core.ILogEventSink
    {
        public List<Serilog.Events.LogEvent> Events { get; } = [];
        public void Emit(Serilog.Events.LogEvent logEvent) { lock (Events) Events.Add(logEvent); }
    }

    [Test]
    public async Task OnConnectedAsync_RejectedConnection_DoesNotCallNetDiagDelivery()
    {
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);
        var hub = CreateHub(friendCommandHandler);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?access_token=not-a-ticket");
        contextAccessor.Setup(a => a.HttpContext).Returns(httpContext);
        SetHubContext(hub, "conn1");

        await hub.OnConnectedAsync();

        netDiagDelivery.Verify(d => d.OnUserConnectedAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task OnConnectedAsync_MissingAccessToken_Rejected_NoConnect()
    {
        // Arrange: no access_token on the query string at all. Ticket-only means "no ticket → no
        // connection": AuthorizationFailed + abort, never registered.
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);
        var hub = CreateHub(friendCommandHandler);

        var httpContext = new DefaultHttpContext();
        contextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        SetHubContext(hub, "conn1");

        // Act
        await hub.OnConnectedAsync();

        // Assert: rejected, never connected, never registered.
        mockCaller.Verify(c => c.SendCoreAsync("AuthorizationFailed", It.IsAny<object[]>(), default), Times.Once);
        mockCaller.Verify(c => c.SendCoreAsync("Connected", It.IsAny<object[]>(), default), Times.Never);
        Assert.That(connections.GetUser("conn1"), Is.Null);
    }

    [Test]
    public async Task OnConnectedAsync_TicketReplayedWhileHeld_Rejected_NoSecondConnect()
    {
        // Arrange: a first connect redeems the ticket and stays open, then the SAME ticket is
        // replayed on a second connect. One holder at a time must hold at the hub boundary, so a
        // sniffed ticket can't open a parallel session: the second connect is rejected.
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        var ticket = ticketStore.Mint(new W3CUserAuthenticationDto { BattleTag = "Ticket#1" }, DateTime.UtcNow);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString($"?access_token={ticket}");
        contextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        // First connect redeems and holds the ticket.
        var firstHub = CreateHub(friendCommandHandler);
        SetHubContext(firstHub, "conn1");
        await firstHub.OnConnectedAsync();
        Assert.That(connections.GetUser("conn1")?.BattleTag, Is.EqualTo("Ticket#1"));

        // Second connect replays the held ticket.
        var secondHub = CreateHub(friendCommandHandler);
        SetHubContext(secondHub, "conn2");
        await secondHub.OnConnectedAsync();

        // Assert: replay rejected — the second connection was never registered.
        Assert.That(connections.GetUser("conn2"), Is.Null);
        mockCaller.Verify(c => c.SendCoreAsync("AuthorizationFailed", It.IsAny<object[]>(), default), Times.Once);

        // The replay revoked the ticket: even after the holder leaves, it can't be redeemed again.
        await firstHub.OnDisconnectedAsync(null);
        var thirdHub = CreateHub(friendCommandHandler);
        SetHubContext(thirdHub, "conn3");
        await thirdHub.OnConnectedAsync();
        Assert.That(connections.GetUser("conn3"), Is.Null);
    }

    [Test]
    public async Task OnConnectedAsync_ReconnectAfterDisconnect_ReusesTicket()
    {
        // Arrange: the launcher's reconnect loop presents the same ticket again after its first
        // connection dropped. Inside the TTL that must work, so a reconnect burst doesn't mint.
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache, friendRequestCache);

        var ticket = ticketStore.Mint(new W3CUserAuthenticationDto { BattleTag = "Ticket#1" }, DateTime.UtcNow);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString($"?access_token={ticket}");
        contextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        var firstHub = CreateHub(friendCommandHandler);
        SetHubContext(firstHub, "conn1");
        await firstHub.OnConnectedAsync();
        await firstHub.OnDisconnectedAsync(null);
        Assert.That(connections.GetUser("conn1"), Is.Null);

        // Act: reconnect with the same ticket.
        var secondHub = CreateHub(friendCommandHandler);
        SetHubContext(secondHub, "conn2");
        await secondHub.OnConnectedAsync();

        // Assert: both connects succeeded with the ticket identity.
        Assert.That(connections.GetUser("conn2")?.BattleTag, Is.EqualTo("Ticket#1"));
        mockCaller.Verify(c => c.SendCoreAsync("Connected", It.IsAny<object[]>(), default), Times.Exactly(2));
        mockCaller.Verify(c => c.SendCoreAsync("AuthorizationFailed", It.IsAny<object[]>(), default), Times.Never);
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
