using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;

namespace WC3ChampionsStatisticService.Tests.LagReports;

/// <summary>
/// Round-trip + filter coverage for the system-derived <see cref="LagReportPlayer.ConnectionIssueTags"/>
/// field. Uses an ISOLATED in-memory mongod (Mongo2Go) per fixture — NOT the shared
/// remote Mongo of <c>IntegrationTestBase</c> — to avoid cross-session contamination.
/// Mirrors the structure of the existing Categories repository tests.
/// </summary>
[TestFixture]
public class LagReportTagRepositoryTests
{
    private MongoDbRunner _runner;
    private MongoClient _mongoClient;
    private LagReportRepository _repo;

    [SetUp]
    public void SetUp()
    {
        _runner = MongoDbRunner.Start();
        _mongoClient = new MongoClient(_runner.ConnectionString);
        _repo = new LagReportRepository(_mongoClient);
    }

    [TearDown]
    public void TearDown()
    {
        _runner.Dispose();
    }

    private static LagReportPlayer CreatePlayer(
        string battleTag, List<ELagReportTag> tags = null) => new()
        {
            BattleTag = battleTag,
            ClientIp = "203.0.113.1",
            ConnectionType = EConnectionType.Direct,
            IssueCategories = [],
            ConnectionIssueTags = tags ?? [],
            FreeText = "",
            Diagnostics = new PlayerDiagnostics(),
        };

    private static LagReport CreateTemplate(int floGameId, int gameId) => new()
    {
        GameId = gameId,
        FloGameId = floGameId,
        GameName = "tag-test-game",
        MapPath = "(2)EchoIsles.w3x",
        ServerNodeId = 1,
        ServerNodeName = "EU West",
    };

    [Test]
    public async Task UpsertPlayerData_PersistsTags()
    {
        var template = CreateTemplate(1001, 5001);
        var player = CreatePlayer("Tagged#1", [ELagReportTag.LastMile]);

        var reportId = await _repo.UpsertPlayerData(template.FloGameId, player, template);
        var report = await _repo.GetById(reportId);

        Assert.AreEqual(1, report.Players.Count);
        Assert.AreEqual(1, report.Players[0].ConnectionIssueTags.Count);
        Assert.AreEqual(ELagReportTag.LastMile, report.Players[0].ConnectionIssueTags[0]);
    }

    [Test]
    public async Task UpsertPlayerData_MergesTagsPerPlayerInSameGame()
    {
        // Two players in one flo game each carry their own (different) verdict tag.
        // They must merge into the SAME document, each retaining its own tags.
        var template = CreateTemplate(1002, 5002);
        var lanPlayer = CreatePlayer("Lan#1", [ELagReportTag.LAN]);
        var lastMilePlayer = CreatePlayer("LastMile#2", [ELagReportTag.LastMile]);

        var id1 = await _repo.UpsertPlayerData(template.FloGameId, lanPlayer, template);
        var id2 = await _repo.UpsertPlayerData(template.FloGameId, lastMilePlayer, template);

        Assert.AreEqual(id1, id2, "both players merge into one per-game document");

        var report = await _repo.GetById(id1);
        Assert.AreEqual(2, report.Players.Count);
        CollectionAssert.Contains(report.Players[0].ConnectionIssueTags, ELagReportTag.LAN);
        CollectionAssert.Contains(report.Players[1].ConnectionIssueTags, ELagReportTag.LastMile);
    }

    [Test]
    public async Task UpsertPlayerData_EmptyTags_RoundTripsAsEmpty()
    {
        // A player with no verdict (Beyond/None/Insufficient) stores an empty
        // tag list and does not break deserialization.
        var template = CreateTemplate(1003, 5003);
        var player = CreatePlayer("Untagged#3");

        var reportId = await _repo.UpsertPlayerData(template.FloGameId, player, template);
        var report = await _repo.GetById(reportId);

        Assert.IsNotNull(report.Players[0].ConnectionIssueTags);
        Assert.IsEmpty(report.Players[0].ConnectionIssueTags);
    }

    [Test]
    public async Task GetReports_FiltersByTag()
    {
        var t1 = CreateTemplate(2001, 6001);
        var t2 = CreateTemplate(2002, 6002);
        await _repo.UpsertPlayerData(t1.FloGameId, CreatePlayer("Lan#1", [ELagReportTag.LAN]), t1);
        await _repo.UpsertPlayerData(t2.FloGameId, CreatePlayer("LastMile#2", [ELagReportTag.LastMile]), t2);

        var (lan, lanTotal) = await _repo.GetReports(new LagReportQueryRequest { Tag = "LAN" });
        Assert.AreEqual(1, lanTotal);
        Assert.AreEqual(1, lan.Count);
        CollectionAssert.Contains(lan.Select(r => r.GameId).ToList(), 6001);
        CollectionAssert.DoesNotContain(lan.Select(r => r.GameId).ToList(), 6002);

        var (lastMile, lmTotal) = await _repo.GetReports(new LagReportQueryRequest { Tag = "LastMile" });
        Assert.AreEqual(1, lmTotal);
        Assert.AreEqual(1, lastMile.Count);
        CollectionAssert.Contains(lastMile.Select(r => r.GameId).ToList(), 6002);
        CollectionAssert.DoesNotContain(lastMile.Select(r => r.GameId).ToList(), 6001);
    }

    [Test]
    public async Task GetReports_TagFilter_IsCaseInsensitive()
    {
        var t = CreateTemplate(2003, 6003);
        await _repo.UpsertPlayerData(t.FloGameId, CreatePlayer("Lan#1", [ELagReportTag.LAN]), t);

        // Enum.TryParse(ignoreCase: true) → "lan" still resolves to LAN.
        var (items, _) = await _repo.GetReports(new LagReportQueryRequest { Tag = "lan" });
        Assert.AreEqual(1, items.Count);
    }

    [Test]
    public async Task EnsureIndexesAsync_CreatesPlayersTagsIndex()
    {
        await _repo.EnsureIndexesAsync();

        var collection = _mongoClient
            .GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<LagReport>("LagReport");

        var names = new List<string>();
        using var cursor = await collection.Indexes.ListAsync();
        foreach (var idx in await cursor.ToListAsync())
        {
            names.Add(idx["name"].AsString);
        }

        Assert.Contains("Players.ConnectionIssueTags_1", names,
            "ConnectionIssueTags must be indexed like Players.IssueCategories for filterable queries");
    }

    [Test]
    public async Task GetReports_ListProjection_IncludesTags()
    {
        // Tags are tiny; unlike the heavy diagnostics arrays they are NOT projected
        // out, so the admin list can show/filter them without a detail fetch.
        var template = CreateTemplate(2100, 6100);
        await _repo.UpsertPlayerData(
            template.FloGameId, CreatePlayer("Lan#1", [ELagReportTag.LAN]), template);

        var (items, _) = await _repo.GetReports(new LagReportQueryRequest());

        Assert.AreEqual(1, items.Count);
        CollectionAssert.Contains(items[0].Players[0].ConnectionIssueTags, ELagReportTag.LAN,
            "ConnectionIssueTags are small and must survive the list projection");
    }
}
