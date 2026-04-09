using System;
using System.Linq;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;

namespace WC3ChampionsStatisticService.Tests.LagReports;

[TestFixture]
public class LagReportControllerTests
{
    // ── Helpers ────────────────────────────────────────────────────────

    private static LagReportSubmissionDto CreateValidDto() => new()
    {
        Diagnostics = new DiagnosticsDataDto
        {
            GameId = 1001,
            PlayerId = 1,
            LagEvents = [new LagEventDto { Timestamp = DateTimeOffset.UtcNow, GameTimeOffsetMs = 300000 }],
            TargetMtr = [new TimedTraceDto
            {
                Timestamp = DateTimeOffset.UtcNow,
                Trace = new TraceResultDto
                {
                    Target = "185.60.112.157",
                    Timestamp = DateTimeOffset.UtcNow,
                    Hops = [new HopDto { HopNumber = 1, Host = "192.168.1.1", AvgRttMs = 1.2, LossPercent = 0 }],
                }
            }],
            AllServerBaselines = [],
            ReverseMtr = [],
            PingHistory = [new TimedPingStatsDto
            {
                Timestamp = DateTimeOffset.UtcNow,
                Stats = new PingStatsDto { Avg = 25, LossRate = 0 },
            }],
            ConnectionEvents = [],
        },
        GameMetadata = new GameMetadataDto
        {
            GameId = 5001,
            FloGameId = 1001,
            GameName = "test-game",
            MapPath = "(2)EchoIsles.w3x",
        },
        ConnectionTopology = new ConnectionTopologyDto
        {
            ServerNodeId = 1,
            ServerNodeName = "EU West",
            ConnectionType = EConnectionType.Direct,
            ClientIp = "203.0.113.1",
        },
        IsExplicit = true,
        Categories = [EIssueCategory.SpikeLag],
        FreeText = "Lag spike at 5 min",
        Annotations = [new AnnotationDto { GameTimeOffsetMs = 300000, Text = "Froze for 2 seconds" }],
    };

    // ── Validation tests ──────────────────────────────────────────────

    [Test]
    public void Validate_ValidDto_ReturnsNull()
    {
        var dto = CreateValidDto();
        Assert.IsNull(LagReportController.ValidateSubmission(dto));
    }

    [Test]
    public void Validate_MissingDiagnostics_ReturnsError()
    {
        var dto = CreateValidDto();
        dto.Diagnostics = null;
        Assert.AreEqual("Missing diagnostics", LagReportController.ValidateSubmission(dto));
    }

    [Test]
    public void Validate_MissingGameMetadata_ReturnsError()
    {
        var dto = CreateValidDto();
        dto.GameMetadata = null;
        Assert.AreEqual("Missing game_metadata", LagReportController.ValidateSubmission(dto));
    }

    [Test]
    public void Validate_MissingConnectionTopology_ReturnsError()
    {
        var dto = CreateValidDto();
        dto.ConnectionTopology = null;
        Assert.AreEqual("Missing connection_topology", LagReportController.ValidateSubmission(dto));
    }

    [Test]
    public void Validate_TooManyLagEvents_ReturnsError()
    {
        var dto = CreateValidDto();
        dto.Diagnostics.LagEvents = Enumerable.Range(0, 201)
            .Select(i => new LagEventDto { Timestamp = DateTimeOffset.UtcNow, GameTimeOffsetMs = i * 1000 })
            .ToList();
        Assert.AreEqual("Too many lag_events", LagReportController.ValidateSubmission(dto));
    }

    [Test]
    public void Validate_TooManyCategories_ReturnsError()
    {
        var dto = CreateValidDto();
        dto.Categories = Enumerable.Range(0, 21).Select(_ => EIssueCategory.Other).ToList();
        Assert.AreEqual("Too many categories", LagReportController.ValidateSubmission(dto));
    }

    [Test]
    public void Validate_FreeTextTooLong_ReturnsError()
    {
        var dto = CreateValidDto();
        dto.FreeText = new string('x', 5001);
        Assert.AreEqual("free_text too long", LagReportController.ValidateSubmission(dto));
    }

    [Test]
    public void Validate_TooManyHopsInTrace_ReturnsError()
    {
        var dto = CreateValidDto();
        dto.Diagnostics.TargetMtr[0].Trace.Hops = Enumerable.Range(0, 65)
            .Select(i => new HopDto { HopNumber = i, LossPercent = 0 })
            .ToList();
        Assert.AreEqual("Too many hops in trace", LagReportController.ValidateSubmission(dto));
    }

    [Test]
    public void Validate_AnnotationTextTooLong_ReturnsError()
    {
        var dto = CreateValidDto();
        dto.Annotations[0].Text = new string('x', 1001);
        Assert.AreEqual("Annotation text too long", LagReportController.ValidateSubmission(dto));
    }

    [Test]
    public void Validate_NullLists_DoesNotThrow()
    {
        var dto = CreateValidDto();
        dto.Diagnostics.LagEvents = null;
        dto.Diagnostics.TargetMtr = null;
        dto.Diagnostics.AllServerBaselines = null;
        dto.Diagnostics.ReverseMtr = null;
        dto.Diagnostics.PingHistory = null;
        dto.Diagnostics.ConnectionEvents = null;
        dto.Annotations = null;
        dto.Categories = null;
        Assert.IsNull(LagReportController.ValidateSubmission(dto));
    }

    [Test]
    public void Validate_ClientIpTooLong_ReturnsError()
    {
        var dto = CreateValidDto();
        dto.ConnectionTopology.ClientIp = new string('x', 101);
        Assert.AreEqual("client_ip too long", LagReportController.ValidateSubmission(dto));
    }

    // ── Mapping tests ─────────────────────────────────────────────────

    [Test]
    public void MapToPlayer_SetsBasicFields()
    {
        var dto = CreateValidDto();
        var player = LagReportController.MapToPlayer(dto, "TestPlayer#1234");

        Assert.AreEqual("TestPlayer#1234", player.BattleTag);
        Assert.AreEqual("203.0.113.1", player.ClientIp);
        Assert.AreEqual(EConnectionType.Direct, player.ConnectionType);
        Assert.IsTrue(player.IsExplicit);
        Assert.AreEqual("Lag spike at 5 min", player.FreeText);
        Assert.AreEqual(1, player.IssueCategories.Count);
        Assert.AreEqual(EIssueCategory.SpikeLag, player.IssueCategories[0]);
    }

    [Test]
    public void MapToPlayer_ParsesProxyAddress_IpAndPort()
    {
        var dto = CreateValidDto();
        dto.ConnectionTopology.ConnectionType = EConnectionType.Proxied;
        dto.ConnectionTopology.ProxyName = "EU-Proxy-1";
        dto.ConnectionTopology.ProxyAddress = "10.0.0.1:8080";

        var player = LagReportController.MapToPlayer(dto, "P#1");

        Assert.AreEqual("10.0.0.1", player.ProxyIp);
        Assert.AreEqual(8080, player.ProxyPort);
        Assert.AreEqual("EU-Proxy-1", player.ProxyName);
    }

    [Test]
    public void MapToPlayer_ParsesProxyAddress_IpOnly()
    {
        var dto = CreateValidDto();
        dto.ConnectionTopology.ProxyAddress = "10.0.0.1";

        var player = LagReportController.MapToPlayer(dto, "P#1");

        Assert.AreEqual("10.0.0.1", player.ProxyIp);
        Assert.IsNull(player.ProxyPort);
    }

    [Test]
    public void MapToPlayer_NullProxyAddress_SetsNulls()
    {
        var dto = CreateValidDto();
        dto.ConnectionTopology.ProxyAddress = null;

        var player = LagReportController.MapToPlayer(dto, "P#1");

        Assert.IsNull(player.ProxyIp);
        Assert.IsNull(player.ProxyPort);
    }

    [Test]
    public void MapToPlayer_MergesAnnotationsIntoLagEvents()
    {
        var dto = CreateValidDto();
        var player = LagReportController.MapToPlayer(dto, "P#1");

        Assert.AreEqual(1, player.Diagnostics.LagEvents.Count);
        Assert.AreEqual("Froze for 2 seconds", player.Diagnostics.LagEvents[0].Annotation);
    }

    [Test]
    public void MapToPlayer_LagEventWithoutAnnotation_HasNullAnnotation()
    {
        var dto = CreateValidDto();
        dto.Annotations = [];

        var player = LagReportController.MapToPlayer(dto, "P#1");

        Assert.IsNull(player.Diagnostics.LagEvents[0].Annotation);
    }

    [Test]
    public void MapToPlayer_NullLists_ProducesEmptyLists()
    {
        var dto = CreateValidDto();
        dto.Diagnostics.LagEvents = null;
        dto.Diagnostics.TargetMtr = null;
        dto.Diagnostics.AllServerBaselines = null;
        dto.Diagnostics.ReverseMtr = null;
        dto.Diagnostics.PingHistory = null;
        dto.Diagnostics.ConnectionEvents = null;
        dto.Annotations = null;
        dto.Categories = null;

        var player = LagReportController.MapToPlayer(dto, "P#1");

        Assert.IsEmpty(player.Diagnostics.LagEvents);
        Assert.IsEmpty(player.Diagnostics.TargetMtr);
        Assert.IsEmpty(player.Diagnostics.AllServerBaselines);
        Assert.IsEmpty(player.Diagnostics.ReverseMtr);
        Assert.IsEmpty(player.Diagnostics.PingHistory);
        Assert.IsEmpty(player.Diagnostics.ConnectionEvents);
        Assert.IsEmpty(player.Annotations);
        Assert.IsEmpty(player.IssueCategories);
    }

    [Test]
    public void MapToPlayer_FiltersOutNullTraces()
    {
        var dto = CreateValidDto();
        dto.Diagnostics.TargetMtr.Add(new TimedTraceDto { Timestamp = DateTimeOffset.UtcNow, Trace = null });
        dto.Diagnostics.ReverseMtr =
        [
            new TimedTraceDto { Timestamp = DateTimeOffset.UtcNow, Trace = null },
        ];
        dto.Diagnostics.AllServerBaselines =
        [
            new ServerTraceDto { Timestamp = DateTimeOffset.UtcNow, ServerId = 1, ServerName = "X", Trace = null },
        ];

        var player = LagReportController.MapToPlayer(dto, "P#1");

        // The valid trace from CreateValidDto survives, the null ones are filtered
        Assert.AreEqual(1, player.Diagnostics.TargetMtr.Count);
        Assert.IsEmpty(player.Diagnostics.ReverseMtr);
        Assert.IsEmpty(player.Diagnostics.AllServerBaselines);
    }

    [Test]
    public void MapToPlayer_MapsDiagnosticsCorrectly()
    {
        var dto = CreateValidDto();
        var player = LagReportController.MapToPlayer(dto, "P#1");

        Assert.AreEqual(1, player.Diagnostics.TargetMtr.Count);
        Assert.AreEqual("185.60.112.157", player.Diagnostics.TargetMtr[0].Target);
        Assert.AreEqual(1, player.Diagnostics.TargetMtr[0].Hops.Count);
        Assert.AreEqual("192.168.1.1", player.Diagnostics.TargetMtr[0].Hops[0].Host);
        Assert.AreEqual(1.2, player.Diagnostics.TargetMtr[0].Hops[0].AvgRttMs);

        Assert.AreEqual(1, player.Diagnostics.PingHistory.Count);
        Assert.AreEqual(25, player.Diagnostics.PingHistory[0].Avg);
    }
}
