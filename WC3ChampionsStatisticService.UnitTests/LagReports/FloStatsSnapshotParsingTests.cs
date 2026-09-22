using System.Text.Json;
using NUnit.Framework;
using W3ChampionsStatisticService.LagReports;

namespace WC3ChampionsStatisticService.Tests.LagReports;

/// <summary>
/// Pins the per-player leave contract between flo-stats and this service.
///
/// Pure unit tests over a JsonElement — no WebSocket, no Mongo, no
/// IntegrationTestBase. Ping-sample parsing is covered separately in
/// FloStatsPingParsingTests.
/// </summary>
[TestFixture]
public class FloStatsSnapshotParsingTests
{
    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement;

    // ── leave reason mapping ──────────────────────────────────────────

    [TestCase("LEAVE_DISCONNECT", EFloLeaveReason.LeaveDisconnect)]
    [TestCase("LEAVE_LOST", EFloLeaveReason.LeaveLost)]
    [TestCase("LEAVE_LOST_BUILDINGS", EFloLeaveReason.LeaveLostBuildings)]
    [TestCase("LEAVE_WON", EFloLeaveReason.LeaveWon)]
    [TestCase("LEAVE_DRAW", EFloLeaveReason.LeaveDraw)]
    [TestCase("LEAVE_OBSERVER", EFloLeaveReason.LeaveObserver)]
    [TestCase("LEAVE_UNKNOWN", EFloLeaveReason.LeaveUnknown)]
    public void MapsEveryKnownWireString(string wire, EFloLeaveReason expected)
    {
        // async-graphql renders enum items as SCREAMING_SNAKE_CASE. If flo ever
        // changes that, this is the test that fails rather than the whole feature
        // silently recording nulls.
        Assert.That(FloStatsService.MapLeaveReason(wire), Is.EqualTo(expected));
    }

    [Test]
    public void AnUnknownVariantMapsToNullButKeepsTheRawString()
    {
        var leaves = FloStatsService.ParsePlayerLeaves(Payload(
            """
            { "game": { "players": [
                { "id": 1, "name": "Player1#1234", "leftAt": 1000, "leaveReason": "LEAVE_SOMETHING_NEW" }
            ] } }
            """));

        Assert.That(leaves, Has.Count.EqualTo(1));
        Assert.That(leaves[0].LeaveReason, Is.Null);
        // Without the raw string a variant flo added later would be indistinguishable
        // from "no leave was recorded", which means the opposite thing.
        Assert.That(leaves[0].LeaveReasonRaw, Is.EqualTo("LEAVE_SOMETHING_NEW"));
    }

    // ── player leaves ─────────────────────────────────────────────────

    [Test]
    public void APlayerWithNoLeaveRecordIsStillEmitted()
    {
        // flo records no PlayerLeft at all when a started game's stream simply
        // closes, so a null reason on a player who was in the game is the signal,
        // not an absence of data.
        var leaves = FloStatsService.ParsePlayerLeaves(Payload(
            """
            { "game": { "players": [
                { "id": 3, "name": "Player3#1234", "leftAt": null, "leaveReason": null }
            ] } }
            """));

        Assert.That(leaves, Has.Count.EqualTo(1));
        Assert.That(leaves[0].PlayerId, Is.EqualTo(3));
        Assert.That(leaves[0].LeaveReason, Is.Null);
        Assert.That(leaves[0].LeaveReasonRaw, Is.Null);
        Assert.That(leaves[0].LeftAtMs, Is.Null);
    }

    [Test]
    public void ParsesAMixedGame()
    {
        var leaves = FloStatsService.ParsePlayerLeaves(Payload(
            """
            { "game": { "players": [
                { "id": 1, "name": "Player1#1234", "leftAt": 412350, "leaveReason": "LEAVE_WON" },
                { "id": 2, "name": "Player2#1234", "leftAt": 412100, "leaveReason": "LEAVE_LOST" },
                { "id": 3, "name": "Player3#1234", "leftAt": null, "leaveReason": null },
                { "id": 4, "name": "Player4#1234", "leftAt": 90000, "leaveReason": "LEAVE_DISCONNECT" }
            ] } }
            """));

        Assert.That(leaves, Has.Count.EqualTo(4));
        Assert.That(leaves[0].LeaveReason, Is.EqualTo(EFloLeaveReason.LeaveWon));
        Assert.That(leaves[0].LeftAtMs, Is.EqualTo(412350));
        Assert.That(leaves[1].LeaveReason, Is.EqualTo(EFloLeaveReason.LeaveLost));
        Assert.That(leaves[2].LeaveReason, Is.Null);
        Assert.That(leaves[3].LeaveReason, Is.EqualTo(EFloLeaveReason.LeaveDisconnect));
        Assert.That(leaves[3].PlayerName, Is.EqualTo("Player4#1234"));
    }

    [Test]
    public void AMissingGameKeyYieldsAnEmptyListRatherThanThrowing()
    {
        // A throw here would abort the whole snapshot parse, and the caller would
        // then persist an empty result that the "already captured" guard treats as
        // final — the exact failure mode the avg bug below caused for ping data.
        Assert.That(FloStatsService.ParsePlayerLeaves(Payload("""{ "stats": {} }""")), Is.Empty);
    }

    [Test]
    public void APlayerWithNoIdIsSkipped()
    {
        var leaves = FloStatsService.ParsePlayerLeaves(Payload(
            """
            { "game": { "players": [
                { "name": "NoId#1234", "leaveReason": "LEAVE_WON" },
                { "id": 2, "name": "Player2#1234", "leaveReason": "LEAVE_LOST" }
            ] } }
            """));

        Assert.That(leaves, Has.Count.EqualTo(1));
        Assert.That(leaves[0].PlayerId, Is.EqualTo(2));
    }
}
