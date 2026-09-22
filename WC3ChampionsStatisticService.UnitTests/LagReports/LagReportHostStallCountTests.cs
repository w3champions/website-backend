using System;
using System.Text.Json;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;

namespace WC3ChampionsStatisticService.Tests.LagReports;

/// <summary>
/// PR #564 kept <see cref="PlayerDiagnostics.HostStalls"/> in the admin list's Mongo
/// projection ("the same diagnostic the list view itself wants") but
/// <see cref="LagReportController.GetReports"/> never mapped it onto
/// <see cref="LagReportPlayerSummary"/> — every other count field made it through, this one
/// was silently dropped. These tests pin the fix: <see cref="LagReportController.MapToSummary"/>
/// (the exact projection GetReports applies to every player) carries a
/// <see cref="LagReportPlayerSummary.HostStallCount"/>, mapped with the same null-tolerant
/// style as LagEventCount/ConnectionEventCount, and its wire name is plain camelCase like
/// those two (no [JsonPropertyName], unlike ConnectionIssueTags on the same class).
/// </summary>
[TestFixture]
public class LagReportHostStallCountTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static LagReportPlayer CreatePlayer(PlayerDiagnostics diagnostics) => new()
    {
        BattleTag = "Stalled#1234",
        ClientIp = "203.0.113.1",
        ConnectionType = EConnectionType.Direct,
        IssueCategories = [],
        ConnectionIssueTags = [],
        FreeText = "",
        Diagnostics = diagnostics,
    };

    private static HostStallData CreateStall() => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        GameTimeOffsetMs = 43762,
        StallMs = 7000,
        PlayersTotal = 8,
        PlayersFlagged = 7,
        Outcome = "mass_lag_suppressed",
    };

    [Test]
    public void MapToSummary_PlayerWithHostStalls_SurfacesCount()
    {
        var player = CreatePlayer(new PlayerDiagnostics { HostStalls = [CreateStall(), CreateStall(), CreateStall()] });

        var summary = LagReportController.MapToSummary(player);

        Assert.That(summary.HostStallCount, Is.EqualTo(3));
    }

    [Test]
    public void MapToSummary_PlayerWithNullHostStalls_YieldsZero_DoesNotThrow()
    {
        var player = CreatePlayer(new PlayerDiagnostics { HostStalls = null });

        var summary = LagReportController.MapToSummary(player);

        Assert.That(summary.HostStallCount, Is.EqualTo(0));
    }

    [Test]
    public void MapToSummary_PlayerWithNullDiagnostics_YieldsZero_DoesNotThrow()
    {
        var player = CreatePlayer(diagnostics: null);

        var summary = LagReportController.MapToSummary(player);

        Assert.That(summary.HostStallCount, Is.EqualTo(0));
    }

    [Test]
    public void LagReportPlayerSummary_SerializesHostStallCount_AsPlainCamelCase()
    {
        // Pins the wire contract directly on the DTO: plain camelCase derived from the naming
        // policy, like LagEventCount — not an explicit [JsonPropertyName] like
        // ConnectionIssueTags carries on this same class.
        var summary = new LagReportPlayerSummary { HostStallCount = 5 };

        var json = JsonSerializer.Serialize(summary, Web);

        Assert.That(json, Does.Contain("\"hostStallCount\":5"));
    }

    [Test]
    public void MapToSummary_Output_RoundTrips_HostStallCount_Through_Wire_Json()
    {
        // End-to-end: the exact object GetReports would put on the wire for a player with
        // host stalls serializes the count under "hostStallCount".
        var player = CreatePlayer(new PlayerDiagnostics { HostStalls = [CreateStall()] });
        var summary = LagReportController.MapToSummary(player);

        var json = JsonSerializer.Serialize(summary, Web);
        using var doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.GetProperty("hostStallCount").GetInt32(), Is.EqualTo(1));
    }
}
