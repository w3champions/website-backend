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
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Filters;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using NUnit.Framework;
using MongoDB.Driver;

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
    public Task UpsertFriendlist(Friendlist friendList) => Task.CompletedTask;
}

// Custom handler that implements IFriendCommandHandler
public class TestFriendCommandHandler : IFriendCommandHandler
{
    private readonly TestFriendRepository _repo;
    private readonly TestFriendListCache _cache;
    public TestFriendCommandHandler(TestFriendRepository repo, TestFriendListCache cache)
    {
        _repo = repo;
        _cache = cache;
    }

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
    public Task DeleteFriendRequest(FriendRequest request) => Task.CompletedTask;

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
    [Test]
    public async Task BlockPlayer_AddsToBlockedBattleTags_WhenNoFriendRequestExists()
    {
        var authService = new Mock<IW3CAuthenticationService>();
        var connections = new ConnectionMapping();
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var friendRequestCache = new FakeFriendRequestCache();
        var personalSettingsRepo = new Mock<IPersonalSettingsRepository>();

        var friendList = new Friendlist("User#1234");
        var friendListCache = new TestFriendListCache();
        friendListCache.Upsert(friendList);
        var friendRepository = new TestFriendRepository();
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache);

        var tracingService = new TracingService(new System.Diagnostics.ActivitySource("test"), new Mock<IHttpContextAccessor>().Object);

        var hub = new WebsiteBackendHub(
            authService.Object,
            connections,
            contextAccessor.Object,
            friendRequestCache,
            personalSettingsRepo.Object,
            friendCommandHandler,
            tracingService
        );

        var testUser = new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" };
        connections.Add("conn1", testUser);

        var mockClients = new Mock<IHubCallerClients>();
        var mockCaller = new Mock<ISingleClientProxy>();
        mockClients.Setup(clients => clients.Caller).Returns(mockCaller.Object);
        typeof(Hub).GetProperty("Clients").SetValue(hub, mockClients.Object);
        typeof(Hub).GetProperty("Context").SetValue(hub, new HubCallerContextMock("conn1"));

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
        var authService = new Mock<IW3CAuthenticationService>();
        var connections = new ConnectionMapping();
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var friendRequestCache = new FakeFriendRequestCache();
        var personalSettingsRepo = new Mock<IPersonalSettingsRepository>();

        var friendList = new Friendlist("User#1234");
        friendList.BlockedBattleTags.Add("Blocked#5678");
        var friendListCache = new TestFriendListCache();
        friendListCache.Upsert(friendList);
        var friendRepository = new TestFriendRepository();
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache);

        var tracingService = new TracingService(new System.Diagnostics.ActivitySource("test"), new Mock<IHttpContextAccessor>().Object);

        var hub = new WebsiteBackendHub(
            authService.Object,
            connections,
            contextAccessor.Object,
            friendRequestCache,
            personalSettingsRepo.Object,
            friendCommandHandler,
            tracingService
        );

        var testUser = new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" };
        connections.Add("conn1", testUser);

        var mockClients = new Mock<IHubCallerClients>();
        var mockCaller = new Mock<ISingleClientProxy>();
        mockClients.Setup(clients => clients.Caller).Returns(mockCaller.Object);
        typeof(Hub).GetProperty("Clients").SetValue(hub, mockClients.Object);
        typeof(Hub).GetProperty("Context").SetValue(hub, new HubCallerContextMock("conn1"));

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
        var authService = new Mock<IW3CAuthenticationService>();
        var connections = new ConnectionMapping();
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var friendRequestCache = new FakeFriendRequestCache();
        var personalSettingsRepo = new Mock<IPersonalSettingsRepository>();

        var friendList = new Friendlist("User#1234");
        friendList.Friends.Add("Friend#5678");
        var friendListCache = new TestFriendListCache();
        friendListCache.Upsert(friendList);
        var friendRepository = new TestFriendRepository();
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache);

        var tracingService = new TracingService(new System.Diagnostics.ActivitySource("test"), new Mock<IHttpContextAccessor>().Object);

        var hub = new WebsiteBackendHub(
            authService.Object,
            connections,
            contextAccessor.Object,
            friendRequestCache,
            personalSettingsRepo.Object,
            friendCommandHandler,
            tracingService
        );

        var testUser = new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" };
        connections.Add("conn1", testUser);

        var mockClients = new Mock<IHubCallerClients>();
        var mockCaller = new Mock<ISingleClientProxy>();
        mockClients.Setup(clients => clients.Caller).Returns(mockCaller.Object);
        typeof(Hub).GetProperty("Clients").SetValue(hub, mockClients.Object);
        typeof(Hub).GetProperty("Context").SetValue(hub, new HubCallerContextMock("conn1"));

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
        var authService = new Mock<IW3CAuthenticationService>();
        var connections = new ConnectionMapping();
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var friendRequestCache = new FakeFriendRequestCache();
        var personalSettingsRepo = new Mock<IPersonalSettingsRepository>();

        var friendList = new Friendlist("User#1234");
        var friendListCache = new TestFriendListCache();
        friendListCache.Upsert(friendList);
        var friendRepository = new TestFriendRepository();
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache);

        var tracingService = new TracingService(new System.Diagnostics.ActivitySource("test"), new Mock<IHttpContextAccessor>().Object);

        var hub = new WebsiteBackendHub(
            authService.Object,
            connections,
            contextAccessor.Object,
            friendRequestCache,
            personalSettingsRepo.Object,
            friendCommandHandler,
            tracingService
        );

        var testUser = new WebSocketUser { BattleTag = "User#1234", ConnectionId = "conn1" };
        connections.Add("conn1", testUser);

        var mockClients = new Mock<IHubCallerClients>();
        var mockCaller = new Mock<ISingleClientProxy>();
        mockClients.Setup(clients => clients.Caller).Returns(mockCaller.Object);
        typeof(Hub).GetProperty("Clients").SetValue(hub, mockClients.Object);
        typeof(Hub).GetProperty("Context").SetValue(hub, new HubCallerContextMock("conn1"));

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

    // Helper mock for HubCallerContext
    private class HubCallerContextMock : HubCallerContext
    {
        private readonly string _connectionId;
        public HubCallerContextMock(string connectionId) => _connectionId = connectionId;
        public override string ConnectionId => _connectionId;
        public override string UserIdentifier => null;
        public override ClaimsPrincipal User => null;
        public override IDictionary<object, object> Items { get; } = new Dictionary<object, object>();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override void Abort() { }
    }

    [Test]
    public async Task MakeFriendRequest_SendsSuccessMessage()
    {
        var authService = new Mock<IW3CAuthenticationService>();
        var connections = new ConnectionMapping();
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var friendRequestCache = new FakeFriendRequestCache();
        var personalSettingsRepo = new Mock<IPersonalSettingsRepository>();
        personalSettingsRepo.Setup(r => r.Find(It.IsAny<string>())).ReturnsAsync(new PersonalSetting("Receiver#1"));

        var friendList = new Friendlist("Sender#1");
        var friendListCache = new TestFriendListCache();
        friendListCache.Upsert(friendList);
        var friendRepository = new TestFriendRepository();
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache);

        var tracingService = new TracingService(new System.Diagnostics.ActivitySource("test"), new Mock<IHttpContextAccessor>().Object);

        var hub = new WebsiteBackendHub(
            authService.Object,
            connections,
            contextAccessor.Object,
            friendRequestCache,
            personalSettingsRepo.Object,
            friendCommandHandler,
            tracingService
        );

        var testUser = new WebSocketUser { BattleTag = "Sender#1", ConnectionId = "conn1" };
        connections.Add("conn1", testUser);

        var mockClients = new Mock<IHubCallerClients>();
        var mockCaller = new Mock<ISingleClientProxy>();
        mockClients.Setup(clients => clients.Caller).Returns(mockCaller.Object);
        typeof(Hub).GetProperty("Clients").SetValue(hub, mockClients.Object);
        typeof(Hub).GetProperty("Context").SetValue(hub, new HubCallerContextMock("conn1"));

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

    [Test]
    public async Task AcceptIncomingFriendRequest_SendsSuccessMessage()
    {
        var authService = new Mock<IW3CAuthenticationService>();
        var connections = new ConnectionMapping();
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var friendRequestCache = new FakeFriendRequestCache();
        var personalSettingsRepo = new Mock<IPersonalSettingsRepository>();

        var friendList = new Friendlist("Receiver#1");
        var friendListCache = new TestFriendListCache();
        friendListCache.Upsert(friendList);
        var friendRepository = new TestFriendRepository();
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache);

        // Add the friend request to the cache so Accept will succeed
        var req = new FriendRequest("Sender#1", "Receiver#1");
        friendRequestCache.AddRequest(req);

        var tracingService = new TracingService(new System.Diagnostics.ActivitySource("test"), new Mock<IHttpContextAccessor>().Object);

        var hub = new WebsiteBackendHub(
            authService.Object,
            connections,
            contextAccessor.Object,
            friendRequestCache,
            personalSettingsRepo.Object,
            friendCommandHandler,
            tracingService
        );

        var testUser = new WebSocketUser { BattleTag = "Receiver#1", ConnectionId = "conn1" };
        connections.Add("conn1", testUser);

        var mockClients = new Mock<IHubCallerClients>();
        var mockCaller = new Mock<ISingleClientProxy>();
        mockClients.Setup(clients => clients.Caller).Returns(mockCaller.Object);
        typeof(Hub).GetProperty("Clients").SetValue(hub, mockClients.Object);
        typeof(Hub).GetProperty("Context").SetValue(hub, new HubCallerContextMock("conn1"));

        await hub.AcceptIncomingFriendRequest(req);

        mockCaller.Verify(
            c => c.SendCoreAsync(
                It.Is<string>(s => s.Contains("FriendResponseData")),
                It.IsAny<object[]>(),
                default
            ),
            Times.AtLeastOnce
        );
    }

    [Test]
    public async Task DenyIncomingFriendRequest_SendsSuccessMessage()
    {
        var authService = new Mock<IW3CAuthenticationService>();
        var connections = new ConnectionMapping();
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var friendRequestCache = new FakeFriendRequestCache();
        var personalSettingsRepo = new Mock<IPersonalSettingsRepository>();

        var friendList = new Friendlist("Receiver#1");
        var friendListCache = new TestFriendListCache();
        friendListCache.Upsert(friendList);
        var friendRepository = new TestFriendRepository();
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache);

        // Add the friend request to the cache so Deny will succeed
        var req = new FriendRequest("Sender#1", "Receiver#1");
        friendRequestCache.AddRequest(req);

        var tracingService = new TracingService(new System.Diagnostics.ActivitySource("test"), new Mock<IHttpContextAccessor>().Object);

        var hub = new WebsiteBackendHub(
            authService.Object,
            connections,
            contextAccessor.Object,
            friendRequestCache,
            personalSettingsRepo.Object,
            friendCommandHandler,
            tracingService
        );

        var testUser = new WebSocketUser { BattleTag = "Receiver#1", ConnectionId = "conn1" };
        connections.Add("conn1", testUser);

        var mockClients = new Mock<IHubCallerClients>();
        var mockCaller = new Mock<ISingleClientProxy>();
        mockClients.Setup(clients => clients.Caller).Returns(mockCaller.Object);
        typeof(Hub).GetProperty("Clients").SetValue(hub, mockClients.Object);
        typeof(Hub).GetProperty("Context").SetValue(hub, new HubCallerContextMock("conn1"));

        await hub.DenyIncomingFriendRequest(req);

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
    public async Task DeleteOutgoingFriendRequest_SendsSuccessMessage()
    {
        var authService = new Mock<IW3CAuthenticationService>();
        var connections = new ConnectionMapping();
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var friendRequestCache = new FakeFriendRequestCache();
        var personalSettingsRepo = new Mock<IPersonalSettingsRepository>();

        var friendList = new Friendlist("Sender#1");
        var friendListCache = new TestFriendListCache();
        friendListCache.Upsert(friendList);
        var friendRepository = new TestFriendRepository();
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache);

        // Add the friend request to the cache so Delete will succeed
        var req = new FriendRequest("Sender#1", "Receiver#1");
        friendRequestCache.AddRequest(req);

        var tracingService = new TracingService(new System.Diagnostics.ActivitySource("test"), new Mock<IHttpContextAccessor>().Object);

        var hub = new WebsiteBackendHub(
            authService.Object,
            connections,
            contextAccessor.Object,
            friendRequestCache,
            personalSettingsRepo.Object,
            friendCommandHandler,
            tracingService
        );

        var testUser = new WebSocketUser { BattleTag = "Sender#1", ConnectionId = "conn1" };
        connections.Add("conn1", testUser);

        var mockClients = new Mock<IHubCallerClients>();
        var mockCaller = new Mock<ISingleClientProxy>();
        mockClients.Setup(clients => clients.Caller).Returns(mockCaller.Object);
        typeof(Hub).GetProperty("Clients").SetValue(hub, mockClients.Object);
        typeof(Hub).GetProperty("Context").SetValue(hub, new HubCallerContextMock("conn1"));

        await hub.DeleteOutgoingFriendRequest(req);

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
    public async Task BlockIncomingFriendRequest_SendsSuccessMessage()
    {
        var authService = new Mock<IW3CAuthenticationService>();
        var connections = new ConnectionMapping();
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var friendRequestCache = new FakeFriendRequestCache();
        var personalSettingsRepo = new Mock<IPersonalSettingsRepository>();

        var friendList = new Friendlist("Receiver#1");
        var friendListCache = new TestFriendListCache();
        friendListCache.Upsert(friendList);
        var friendRepository = new TestFriendRepository();
        IFriendCommandHandler friendCommandHandler = new TestFriendCommandHandler(friendRepository, friendListCache);

        // Add the friend request to the cache so Block will succeed
        var req = new FriendRequest("Sender#1", "Receiver#1");
        friendRequestCache.AddRequest(req);

        var tracingService = new TracingService(new System.Diagnostics.ActivitySource("test"), new Mock<IHttpContextAccessor>().Object);

        var hub = new WebsiteBackendHub(
            authService.Object,
            connections,
            contextAccessor.Object,
            friendRequestCache,
            personalSettingsRepo.Object,
            friendCommandHandler,
            tracingService
        );

        var testUser = new WebSocketUser { BattleTag = "Receiver#1", ConnectionId = "conn1" };
        connections.Add("conn1", testUser);

        var mockClients = new Mock<IHubCallerClients>();
        var mockCaller = new Mock<ISingleClientProxy>();
        mockClients.Setup(clients => clients.Caller).Returns(mockCaller.Object);
        typeof(Hub).GetProperty("Clients").SetValue(hub, mockClients.Object);
        typeof(Hub).GetProperty("Context").SetValue(hub, new HubCallerContextMock("conn1"));

        await hub.BlockIncomingFriendRequest(req);

        mockCaller.Verify(
            c => c.SendCoreAsync(
                It.Is<string>(s => s.Contains("FriendResponseData")),
                It.IsAny<object[]>(),
                default
            ),
            Times.Once
        );
    }
}
