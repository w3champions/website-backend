using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Friends;
using W3ChampionsStatisticService.Hubs;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Services;

namespace WC3ChampionsStatisticService.Tests.Ranks;

[TestFixture]
public class FriendRankPromotionTests : IntegrationTestBase
{
    private List<(string ConnectionId, string Method, object[] Args)> _sends;
    private bool _throwOnSend;
    private HashSet<string> _throwForConnections;
    private ConnectionMapping _connections;
    private RankRepository _rankRepository;
    private LeagueBaselineRepository _baselineRepository;
    private FriendRepository _friendRepository;
    private RankSyncHandler _handler;

    private void SetupHarness(bool throwOnSend = false)
    {
        _sends = [];
        _throwOnSend = throwOnSend;
        _throwForConnections = [];
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Client(It.IsAny<string>())).Returns((string connectionId) =>
        {
            var proxy = new Mock<ISingleClientProxy>();
            proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                .Callback((string method, object[] args, CancellationToken _) =>
                {
                    if (_throwOnSend || _throwForConnections.Contains(connectionId)) throw new InvalidOperationException("hub send failed");
                    _sends.Add((connectionId, method, args));
                })
                .Returns(Task.CompletedTask);
            return proxy.Object;
        });
        var hubContext = new Mock<IHubContext<WebsiteBackendHub>>();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);

        _connections = new ConnectionMapping();
        _rankRepository = new RankRepository(MongoClient, personalSettingsProvider);
        _baselineRepository = new LeagueBaselineRepository(MongoClient);
        _friendRepository = new FriendRepository(MongoClient);
        var notifier = new FriendRankPromotionNotifier(
            _baselineRepository, _rankRepository, _friendRepository, _connections, hubContext.Object,
            new Mock<ITrackingService>().Object);
        _handler = new RankSyncHandler(_rankRepository, new MatchEventRepository(MongoClient), notifier);
    }

    private async Task SeedOneVsOneConstellation()
    {
        await _rankRepository.InsertLeagues([
            new LeagueConstellation(0, GateWay.America, GameMode.GM_1v1, [
                new League(0, 0, "Grandmaster", 0),
                new League(1, 1, "Master", 0),
                new League(2, 2, "Diamond", 1),
                new League(3, 1, "Master", 2),
            ])
        ]);
    }

    private async Task BefriendAndConnect()
    {
        // bob is friends with peTer and online twice, carol is friends but offline,
        // dave is online but unrelated.
        await _friendRepository.UpsertFriendlist(new Friendlist("bob#1") { Friends = ["peTer#123"] });
        await _friendRepository.UpsertFriendlist(new Friendlist("carol#2") { Friends = ["peTer#123"] });
        await _friendRepository.UpsertFriendlist(new Friendlist("dave#3") { Friends = ["someoneElse#9"] });
        _connections.Add("conn-bob-1", new WebSocketUser { BattleTag = "bob#1", ConnectionId = "conn-bob-1" });
        _connections.Add("conn-bob-2", new WebSocketUser { BattleTag = "bob#1", ConnectionId = "conn-bob-2" });
        _connections.Add("conn-dave", new WebSocketUser { BattleTag = "dave#3", ConnectionId = "conn-dave" });
    }

    private async Task SyncLeague(int eventId, int league)
    {
        var changedEvent = TestDtoHelper.CreateRankChangedEvent("peTer#123");
        changedEvent.id = eventId;
        changedEvent.league = league;
        await InsertRankChangedEvent(changedEvent);
        await _handler.Update();
    }

    private async Task<LeagueBaseline> LoadBaseline(string id)
    {
        return (await _baselineRepository.LoadByIds([id])).SingleOrDefault();
    }

    [Test]
    public async Task Promotion_PushesToEveryConnectionOfOnlineFriendsOnly()
    {
        SetupHarness();
        await SeedOneVsOneConstellation();
        await BefriendAndConnect();

        await SyncLeague(eventId: 1, league: 1);
        Assert.AreEqual(0, _sends.Count); // first sighting records the baseline silently

        await SyncLeague(eventId: 2, league: 0);

        Assert.AreEqual(2, _sends.Count);
        CollectionAssert.AreEquivalent(new[] { "conn-bob-1", "conn-bob-2" }, _sends.Select(s => s.ConnectionId));
        Assert.IsTrue(_sends.All(s => s.Method == "FriendRankPromoted"));

        var payload = (FriendRankPromotedEvent)_sends[0].Args[0];
        Assert.AreEqual("peTer#123", payload.BattleTag);
        Assert.AreEqual(0, payload.Season);
        Assert.AreEqual(GameMode.GM_1v1, payload.GameMode);
        Assert.AreEqual("Grandmaster", payload.LeagueName);
        Assert.AreEqual(0, payload.LeagueOrder);
        Assert.AreEqual("Master", payload.OldLeagueName);
        Assert.AreEqual(1, payload.OldLeagueOrder);
    }

    [Test]
    public async Task DemotionAndSameTierShuffle_StaySilent_ButAdvanceTheBaseline()
    {
        SetupHarness();
        await SeedOneVsOneConstellation();
        await BefriendAndConnect();

        await SyncLeague(eventId: 1, league: 1);
        await SyncLeague(eventId: 2, league: 3); // Master div 1 -> Master div 2: same order
        await SyncLeague(eventId: 3, league: 2); // Master -> Diamond: demotion

        Assert.AreEqual(0, _sends.Count);
        // Silent transitions still advance the baseline: a later promotion must diff
        // against Diamond, not against the long-gone Master standing.
        var baseline = await LoadBaseline("0_peTer#123@10_GM_1v1");
        Assert.AreEqual(2, baseline.League);
    }

    [Test]
    public async Task TeamStandings_AreNotAnnounced_AndGetNoBaseline()
    {
        SetupHarness();
        await _rankRepository.InsertLeagues([
            new LeagueConstellation(0, GateWay.America, GameMode.GM_2v2_AT, [
                new League(0, 0, "Grandmaster", 0),
                new League(1, 1, "Master", 0),
            ])
        ]);
        await BefriendAndConnect();

        foreach (var (eventId, league) in new[] { (1, 1), (2, 0) })
        {
            await InsertRankChangedEvent(new RankingChangedEvent
            {
                id = eventId,
                league = league,
                gameMode = GameMode.GM_2v2_AT,
                gateway = GateWay.America,
                season = 0,
                ranks = [new RankRaw { rp = 14, battleTags = ["peTer#123", "wolf#456"] }]
            });
            await _handler.Update();
        }

        Assert.AreEqual(0, _sends.Count);
        Assert.IsNull(await LoadBaseline("0_peTer#123@10_wolf#456@10_GM_2v2_AT"));
    }

    [Test]
    public async Task MissingLeagueConstellation_IsSilentButAdvancesTheBaseline()
    {
        SetupHarness();
        await BefriendAndConnect();

        await SyncLeague(eventId: 1, league: 1);
        await SyncLeague(eventId: 2, league: 0);

        Assert.AreEqual(0, _sends.Count);
        // The baseline advances even when the transition cannot be announced — it must
        // not resurface as a promotion once the constellation appears later.
        var baseline = await LoadBaseline("0_peTer#123@10_GM_1v1");
        Assert.AreEqual(0, baseline.League);
    }

    [Test]
    public async Task PushFailure_IsSwallowed_AndNeverReannounced()
    {
        SetupHarness(throwOnSend: true);
        await SeedOneVsOneConstellation();
        await BefriendAndConnect();

        await SyncLeague(eventId: 1, league: 1);
        await SyncLeague(eventId: 2, league: 0);

        Assert.AreEqual(0, _sends.Count);
        // Baselines advance BEFORE pushes: delivery is at most once.
        var baseline = await LoadBaseline("0_peTer#123@10_GM_1v1");
        Assert.AreEqual(0, baseline.League);

        // Had the failed push left a stale baseline (Master), this re-sync of the same
        // Grandmaster standing would announce Master -> Grandmaster again.
        _throwOnSend = false;
        await SyncLeague(eventId: 3, league: 0);
        Assert.AreEqual(0, _sends.Count);
    }

    [Test]
    public async Task OneFailingPush_DoesNotAbortTheRestOfTheBatch()
    {
        SetupHarness();
        await SeedOneVsOneConstellation();
        await BefriendAndConnect();
        // wolf promotes in the same batch as peTer; erin is wolf's online friend.
        await _friendRepository.UpsertFriendlist(new Friendlist("erin#4") { Friends = ["wolf#456"] });
        _connections.Add("conn-erin", new WebSocketUser { BattleTag = "erin#4", ConnectionId = "conn-erin" });

        // Baselines for both standings (Master), silently.
        foreach (var (eventId, battleTag) in new[] { (1, "peTer#123"), (2, "wolf#456") })
        {
            var baselineEvent = TestDtoHelper.CreateRankChangedEvent(battleTag);
            baselineEvent.id = eventId;
            baselineEvent.league = 1;
            await InsertRankChangedEvent(baselineEvent);
        }
        await _handler.Update();
        Assert.AreEqual(0, _sends.Count);

        // Both promote to Grandmaster in ONE batch; every send to peTer's friend
        // bob dies. peTer is processed first (event order), so without per-rank
        // isolation the thrown send would abort wolf's fan-out too.
        _throwForConnections = ["conn-bob-1", "conn-bob-2"];
        foreach (var (eventId, battleTag) in new[] { (3, "peTer#123"), (4, "wolf#456") })
        {
            var promotionEvent = TestDtoHelper.CreateRankChangedEvent(battleTag);
            promotionEvent.id = eventId;
            promotionEvent.league = 0;
            await InsertRankChangedEvent(promotionEvent);
        }
        await _handler.Update();

        // wolf's promotion still reached erin, and peTer's baseline advanced
        // regardless — the failed push is spent (at-most-once), never retried.
        Assert.AreEqual(1, _sends.Count);
        Assert.AreEqual("conn-erin", _sends[0].ConnectionId);
        Assert.AreEqual("wolf#456", ((FriendRankPromotedEvent)_sends[0].Args[0]).BattleTag);
        Assert.AreEqual(0, (await LoadBaseline("0_peTer#123@10_GM_1v1")).League);
    }

    // Lives here rather than in FriendRepositoryTests because that fixture runs on Mongo2Go,
    // and index assertions belong on the same Mongo the promotion pipeline tests use.
    [Test]
    public async Task EnsureFriendlistIndexes_CreateTheFriendsMultikeyIndex_Idempotently()
    {
        SetupHarness();

        await _friendRepository.EnsureIndexesAsync();
        // Re-running must be a no-op, not a conflict (startup runs this on every deploy).
        Assert.DoesNotThrowAsync(async () => await _friendRepository.EnsureIndexesAsync());

        var collection = MongoClient
            .GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<MongoDB.Bson.BsonDocument>("Friendlist");
        var names = new List<string>();
        using (var cursor = await collection.Indexes.ListAsync())
        {
            foreach (var index in await cursor.ToListAsync())
            {
                names.Add(index["name"].AsString);
            }
        }
        Assert.Contains("Friends_1", names);
    }

    [Test]
    public async Task BackloggedBatch_DiffsAgainstTheLastOccurrence()
    {
        SetupHarness();
        await SeedOneVsOneConstellation();
        await BefriendAndConnect();

        await SyncLeague(eventId: 1, league: 2); // baseline: Diamond

        // Two roster events for the same standing land in ONE checkout batch;
        // the last write (Grandmaster) is what gets persisted and announced.
        foreach (var (eventId, league) in new[] { (2, 1), (3, 0) })
        {
            var changedEvent = TestDtoHelper.CreateRankChangedEvent("peTer#123");
            changedEvent.id = eventId;
            changedEvent.league = league;
            await InsertRankChangedEvent(changedEvent);
        }
        await _handler.Update();

        Assert.AreEqual(2, _sends.Count); // one push per bob connection, single promotion
        var payload = (FriendRankPromotedEvent)_sends[0].Args[0];
        Assert.AreEqual("Grandmaster", payload.LeagueName);
        Assert.AreEqual("Diamond", payload.OldLeagueName);
        Assert.AreEqual(2, payload.OldLeagueOrder);
    }
}
