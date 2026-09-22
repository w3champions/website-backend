using System;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;

namespace WC3ChampionsStatisticService.Tests.LagReports;

/// <summary>
/// Persistence round-trip for the node-reported host stall: DTO → model → stored document.
/// Uses an ISOLATED in-memory mongod (Mongo2Go) per fixture — NOT the shared remote Mongo of
/// <c>IntegrationTestBase</c> — to avoid cross-session contamination. Mirrors the structure of
/// <c>LagReportTagRepositoryTests</c>.
/// </summary>
[TestFixture]
public class LagReportHostStallRepositoryTests
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

    private static readonly DateTimeOffset StallAt = DateTimeOffset.Parse("2026-09-21T20:11:08.011758Z");

    private static LagReportSubmissionDto CreateDto(int floGameId, int gameId, params HostStallDto[] stalls) => new()
    {
        Diagnostics = new DiagnosticsDataDto
        {
            GameId = gameId,
            PlayerId = 1,
            LagEvents = [],
            TargetMtr = [],
            AllServerBaselines = [],
            ReverseMtr = [],
            PingHistory = [],
            ConnectionEvents = [],
            HostStalls = [.. stalls],
        },
        GameMetadata = new GameMetadataDto
        {
            GameId = gameId,
            FloGameId = floGameId,
            GameName = "host-stall-test-game",
            MapPath = "(2)EchoIsles.w3x",
        },
        ConnectionTopology = new ConnectionTopologyDto
        {
            ServerNodeId = 1,
            ServerNodeName = "EU West",
            ConnectionType = EConnectionType.Direct,
            ClientIp = "203.0.113.1",
        },
        IsExplicit = false,
        Categories = [],
        ConnectionIssueTags = [],
        FreeText = "",
        Annotations = [],
    };

    private static HostStallDto CreateHostStall(string outcome = "mass_lag_suppressed") => new()
    {
        Timestamp = StallAt,
        GameTimeOffsetMs = 43762,
        StallMs = 7000,
        PlayersTotal = 8,
        PlayersFlagged = 7,
        Outcome = outcome,
    };

    private static LagReport TemplateFor(LagReportSubmissionDto dto) => new()
    {
        GameId = dto.GameMetadata.GameId,
        FloGameId = dto.GameMetadata.FloGameId,
        GameName = dto.GameMetadata.GameName,
        MapPath = dto.GameMetadata.MapPath,
        ServerNodeId = dto.ConnectionTopology.ServerNodeId,
        ServerNodeName = dto.ConnectionTopology.ServerNodeName,
    };

    /// <summary>
    /// The controller's static helpers are the integration seam: they encapsulate exactly the
    /// DTO→domain transformation that SubmitReport performs before calling the repository.
    /// </summary>
    private async Task<LagReport> SubmitAndFetch(LagReportSubmissionDto dto, string battleTag)
    {
        Assert.IsNull(LagReportController.ValidateSubmission(dto));
        var player = LagReportController.MapToPlayer(dto, battleTag);
        var template = TemplateFor(dto);

        var reportId = await _repo.UpsertPlayerData(template.FloGameId, player, template);
        return await _repo.GetById(reportId);
    }

    [Test]
    public async Task Submission_With_HostStalls_RoundTrips_To_StoredDocument()
    {
        var dto = CreateDto(1001, 5001, CreateHostStall());

        var report = await SubmitAndFetch(dto, "Stalled#1234");

        Assert.That(report, Is.Not.Null);
        Assert.That(report.Players, Has.Count.EqualTo(1));

        var stalls = report.Players[0].Diagnostics.HostStalls;
        Assert.That(stalls, Has.Count.EqualTo(1));
        Assert.That(stalls[0].Timestamp, Is.EqualTo(StallAt));
        Assert.That(stalls[0].GameTimeOffsetMs, Is.EqualTo(43762));
        Assert.That(stalls[0].StallMs, Is.EqualTo(7000));
        Assert.That(stalls[0].PlayersTotal, Is.EqualTo(8));
        Assert.That(stalls[0].PlayersFlagged, Is.EqualTo(7));
        Assert.That(stalls[0].Outcome, Is.EqualTo("mass_lag_suppressed"));
    }

    [Test]
    public async Task Submission_WITHOUT_HostStalls_Still_Succeeds()
    {
        // The compatibility guarantee: a launcher that predates the node signal sends no
        // host_stalls at all and must still store a complete, readable report.
        var dto = CreateDto(1002, 5002);

        var report = await SubmitAndFetch(dto, "OldLauncher#1234");

        Assert.That(report, Is.Not.Null);
        Assert.That(report.Players, Has.Count.EqualTo(1));
        Assert.That(report.Players[0].Diagnostics.HostStalls, Is.Not.Null);
        Assert.That(report.Players[0].Diagnostics.HostStalls, Is.Empty);
    }

    [Test]
    public async Task Unrecognised_Outcome_Is_Stored_Verbatim_Because_Outcome_Is_Not_An_Enum()
    {
        // A newer node ships a new outcome value. With a C# enum + JsonStringEnumConverter
        // this would 400 the whole submission and lose the report; as a string it must reach
        // the document unchanged so the value is still queryable after the fact.
        var dto = CreateDto(1003, 5003, CreateHostStall("some_outcome_this_build_has_never_heard_of"));

        var report = await SubmitAndFetch(dto, "FutureNode#1234");

        Assert.That(report.Players[0].Diagnostics.HostStalls[0].Outcome,
            Is.EqualTo("some_outcome_this_build_has_never_heard_of"));
    }

    [Test]
    public async Task GetReports_ListProjection_IncludesHostStalls()
    {
        // HostStalls is small and is the signal the admin list exists to surface, so unlike
        // the MTR/ping arrays it must survive the list projection.
        var dto = CreateDto(1004, 5004, CreateHostStall());
        await SubmitAndFetch(dto, "Stalled#1234");

        var (items, _) = await _repo.GetReports(new LagReportQueryRequest());

        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].Players[0].Diagnostics.HostStalls, Has.Count.EqualTo(1),
            "HostStalls are small and must survive the list projection");
        Assert.That(items[0].Players[0].Diagnostics.HostStalls[0].StallMs, Is.EqualTo(7000));
    }
}
