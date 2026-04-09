using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;

namespace WC3ChampionsStatisticService.Tests.LagReports;

[TestFixture]
public class LagReportRepositoryTests : IntegrationTestBase
{
    private LagReportRepository _repo;

    [SetUp]
    public void SetUp()
    {
        _repo = new LagReportRepository(MongoClient);
    }

    private static LagReportPlayer CreatePlayer(string battleTag, bool isExplicit = false) => new()
    {
        BattleTag = battleTag,
        ClientIp = "203.0.113.1",
        ConnectionType = EConnectionType.Direct,
        IsExplicit = isExplicit,
        IssueCategories = isExplicit ? [EIssueCategory.SpikeLag] : [],
        FreeText = isExplicit ? "Lag spike at 5 min" : "",
        Diagnostics = new PlayerDiagnostics
        {
            LagEvents = [new LagEvent { Timestamp = DateTimeOffset.UtcNow, GameTimeOffsetMs = 300000 }],
            PingHistory = [new PingSample { Timestamp = DateTimeOffset.UtcNow, Avg = 25, LossRate = 0 }],
        },
    };

    private static LagReport CreateTemplate(int floGameId = 1001, int gameId = 5001) => new()
    {
        GameId = gameId,
        FloGameId = floGameId,
        GameName = "test-game",
        MapPath = "(2)EchoIsles.w3x",
        ServerNodeId = 1,
        ServerNodeName = "EU West",
    };

    [Test]
    public async Task UpsertPlayerData_CreatesNewReport()
    {
        var template = CreateTemplate();
        var player = CreatePlayer("Player1#1234", isExplicit: true);

        var reportId = await _repo.UpsertPlayerData(template.FloGameId, player, template);

        Assert.IsNotNull(reportId);

        var report = await _repo.GetById(reportId);
        Assert.IsNotNull(report);
        Assert.AreEqual(template.FloGameId, report.FloGameId);
        Assert.AreEqual(template.GameId, report.GameId);
        Assert.AreEqual(template.GameName, report.GameName);
        Assert.AreEqual(1, report.Players.Count);
        Assert.AreEqual("Player1#1234", report.Players[0].BattleTag);
        Assert.IsTrue(report.HasExplicitReport);
    }

    [Test]
    public async Task UpsertPlayerData_SecondPlayerMergesIntoSameDocument()
    {
        var template = CreateTemplate();
        var player1 = CreatePlayer("Player1#1234");
        var player2 = CreatePlayer("Player2#5678", isExplicit: true);

        var id1 = await _repo.UpsertPlayerData(template.FloGameId, player1, template);
        var id2 = await _repo.UpsertPlayerData(template.FloGameId, player2, template);

        Assert.AreEqual(id1, id2);

        var report = await _repo.GetById(id1);
        Assert.AreEqual(2, report.Players.Count);
        Assert.IsTrue(report.HasExplicitReport);
    }

    [Test]
    public async Task UpsertPlayerData_AutoSubmitDoesNotSetExplicit()
    {
        var template = CreateTemplate();
        var player = CreatePlayer("Player1#1234", isExplicit: false);

        var reportId = await _repo.UpsertPlayerData(template.FloGameId, player, template);

        var report = await _repo.GetById(reportId);
        Assert.IsFalse(report.HasExplicitReport);
    }

    [Test]
    public async Task GetByFloGameId_ReturnsCorrectReport()
    {
        var template = CreateTemplate(floGameId: 9999);
        var player = CreatePlayer("Player1#1234");
        await _repo.UpsertPlayerData(template.FloGameId, player, template);

        var report = await _repo.GetByFloGameId(9999);
        Assert.IsNotNull(report);
        Assert.AreEqual(9999, report.FloGameId);

        var missing = await _repo.GetByFloGameId(8888);
        Assert.IsNull(missing);
    }

    [Test]
    public async Task UpdateServerSidePing_StoresData()
    {
        var template = CreateTemplate();
        var player = CreatePlayer("Player1#1234");
        var reportId = await _repo.UpsertPlayerData(template.FloGameId, player, template);

        var pingData = new List<ServerSidePingData>
        {
            new() { PlayerId = 1, PlayerName = "Player1#1234", Samples = [new ServerPingSample { Time = 60, Avg = 22 }] }
        };
        await _repo.UpdateServerSidePing(reportId, pingData);

        var report = await _repo.GetById(reportId);
        Assert.IsNotNull(report.ServerSidePing);
        Assert.AreEqual(1, report.ServerSidePing.Count);
        Assert.AreEqual(22, report.ServerSidePing[0].Samples[0].Avg);
    }

    [Test]
    public async Task GetReports_FiltersByBattleTag()
    {
        var t1 = CreateTemplate(floGameId: 2001, gameId: 6001);
        var t2 = CreateTemplate(floGameId: 2002, gameId: 6002);
        await _repo.UpsertPlayerData(t1.FloGameId, CreatePlayer("Alice#1111"), t1);
        await _repo.UpsertPlayerData(t2.FloGameId, CreatePlayer("Bob#2222"), t2);

        var (items, total) = await _repo.GetReports(new LagReportQueryRequest { BattleTag = "Alice#1111" });
        Assert.AreEqual(1, total);
        Assert.AreEqual(1, items.Count);
        Assert.AreEqual(2001, items[0].FloGameId);
    }

    [Test]
    public async Task GetReports_FiltersByServerNodeId()
    {
        var t1 = CreateTemplate(floGameId: 3001, gameId: 7001);
        t1.ServerNodeId = 10;
        var t2 = CreateTemplate(floGameId: 3002, gameId: 7002);
        t2.ServerNodeId = 20;
        await _repo.UpsertPlayerData(t1.FloGameId, CreatePlayer("P1#1"), t1);
        await _repo.UpsertPlayerData(t2.FloGameId, CreatePlayer("P2#2"), t2);

        var (items, _) = await _repo.GetReports(new LagReportQueryRequest { ServerNodeId = 10 });
        Assert.AreEqual(1, items.Count);
        Assert.AreEqual(10, items[0].ServerNodeId);
    }

    [Test]
    public async Task GetReports_FiltersByExplicitOnly()
    {
        var t1 = CreateTemplate(floGameId: 4001, gameId: 8001);
        var t2 = CreateTemplate(floGameId: 4002, gameId: 8002);
        await _repo.UpsertPlayerData(t1.FloGameId, CreatePlayer("P1#1", isExplicit: true), t1);
        await _repo.UpsertPlayerData(t2.FloGameId, CreatePlayer("P2#2", isExplicit: false), t2);

        var (items, _) = await _repo.GetReports(new LagReportQueryRequest { ExplicitOnly = true });
        Assert.AreEqual(1, items.Count);
        Assert.IsTrue(items[0].HasExplicitReport);
    }

    [Test]
    public async Task GetReports_FiltersByProxyName()
    {
        var t1 = CreateTemplate(floGameId: 5001, gameId: 9001);
        var t2 = CreateTemplate(floGameId: 5002, gameId: 9002);
        var proxied = CreatePlayer("P1#1");
        proxied.ProxyName = "EU-Proxy-1";
        proxied.ProxyIp = "10.0.0.1";
        await _repo.UpsertPlayerData(t1.FloGameId, proxied, t1);
        await _repo.UpsertPlayerData(t2.FloGameId, CreatePlayer("P2#2"), t2);

        var (items, _) = await _repo.GetReports(new LagReportQueryRequest { ProxyName = "EU-Proxy-1" });
        Assert.AreEqual(1, items.Count);
    }

    [Test]
    public async Task GetReports_FiltersByProxyIp()
    {
        var t1 = CreateTemplate(floGameId: 5003, gameId: 9003);
        var t2 = CreateTemplate(floGameId: 5004, gameId: 9004);
        var proxied = CreatePlayer("P1#1");
        proxied.ProxyIp = "10.0.0.99";
        await _repo.UpsertPlayerData(t1.FloGameId, proxied, t1);
        await _repo.UpsertPlayerData(t2.FloGameId, CreatePlayer("P2#2"), t2);

        var (items, _) = await _repo.GetReports(new LagReportQueryRequest { ProxyIp = "10.0.0.99" });
        Assert.AreEqual(1, items.Count);
    }

    [Test]
    public async Task GetReports_FiltersByIssueCategory()
    {
        var t1 = CreateTemplate(floGameId: 6001, gameId: 10001);
        var t2 = CreateTemplate(floGameId: 6002, gameId: 10002);
        var player1 = CreatePlayer("P1#1");
        player1.IssueCategories = [EIssueCategory.Reconnecting];
        await _repo.UpsertPlayerData(t1.FloGameId, player1, t1);
        await _repo.UpsertPlayerData(t2.FloGameId, CreatePlayer("P2#2"), t2);

        var (items, _) = await _repo.GetReports(new LagReportQueryRequest { IssueCategory = "Reconnecting" });
        Assert.AreEqual(1, items.Count);
    }

    [Test]
    public async Task GetReports_Pagination()
    {
        for (int i = 0; i < 5; i++)
        {
            var t = CreateTemplate(floGameId: 7000 + i, gameId: 11000 + i);
            await _repo.UpsertPlayerData(t.FloGameId, CreatePlayer($"P{i}#1"), t);
        }

        var (page0, total) = await _repo.GetReports(new LagReportQueryRequest { Page = 0, PageSize = 2 });
        Assert.AreEqual(5, total);
        Assert.AreEqual(2, page0.Count);

        var (page1, _) = await _repo.GetReports(new LagReportQueryRequest { Page = 1, PageSize = 2 });
        Assert.AreEqual(2, page1.Count);

        var (page2, _) = await _repo.GetReports(new LagReportQueryRequest { Page = 2, PageSize = 2 });
        Assert.AreEqual(1, page2.Count);
    }

    [Test]
    public async Task GetReports_SortsByCreatedAtDescending()
    {
        var t1 = CreateTemplate(floGameId: 8001, gameId: 12001);
        var t2 = CreateTemplate(floGameId: 8002, gameId: 12002);
        await _repo.UpsertPlayerData(t1.FloGameId, CreatePlayer("P1#1"), t1);
        await Task.Delay(50); // ensure distinct CreatedAt
        await _repo.UpsertPlayerData(t2.FloGameId, CreatePlayer("P2#2"), t2);

        var (items, _) = await _repo.GetReports(new LagReportQueryRequest());
        Assert.IsTrue(items[0].CreatedAt >= items[1].CreatedAt);
    }
}
