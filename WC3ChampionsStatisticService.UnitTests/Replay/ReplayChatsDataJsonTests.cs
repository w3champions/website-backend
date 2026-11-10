using System.Text.Json;
using NUnit.Framework;
using W3C.Contracts.Replay;

namespace WC3ChampionsStatisticService.Tests.Replay;

// The chat log travels replay-service -> website-backend (Newtonsoft, ReplayServiceClient.GetChatLogs)
// -> admin page (System.Text.Json, ReplaysController). replay-service spells a private message's
// recipient `to_player_id` (handlers/chats.rs), which the Newtonsoft read used to drop, so the page
// never showed who a private message went to.
[TestFixture]
public class ReplayChatsDataJsonTests
{
    // Mirrors the JsonSerializerOptions ASP.NET Core uses for responses.
    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    // The body replay-service answers for GET /chats/{gameId}, as serde writes it.
    private const string ReplayServiceBody = """
    {
      "players": [
        { "id": 1, "name": "p1#1", "team": 0, "color": 1 },
        { "id": 2, "name": "p2#2", "team": 1, "color": 2 }
      ],
      "messages": [
        { "time": 1000, "game_time": 900, "from_player": 1, "scope": { "type": "all" }, "content": "p1 all" },
        { "time": 1500, "game_time": 1400, "from_player": 2, "scope": { "type": "player", "to_player_id": 1 }, "content": "p2 to p1" }
      ],
      "events": [
        { "type": "pause", "time": 2000, "game_time": 1800, "player_id": 1 },
        { "type": "leave", "time": 9000, "game_time": 8000, "player_id": 2, "leave_reason": "lost_buildings" }
      ]
    }
    """;

    [Test]
    public void Replay_service_read_keeps_the_private_message_recipient()
    {
        var data = Newtonsoft.Json.JsonConvert.DeserializeObject<ReplayChatsData>(ReplayServiceBody);

        Assert.That(data.Messages[1].Scope.Type, Is.EqualTo(ReplayChatsScopeType.Player));
        Assert.That(data.Messages[1].Scope.Id, Is.EqualTo(1));
    }

    [Test]
    public void Chat_log_response_uses_the_names_the_admin_page_reads()
    {
        var data = Newtonsoft.Json.JsonConvert.DeserializeObject<ReplayChatsData>(ReplayServiceBody);

        var json = JsonSerializer.Serialize(data, WebDefaults);

        // website: src/store/admin/types.ts (ReplayMessage, ReplayMessageScope, ReplayGameEvent).
        Assert.That(json, Does.Contain("\"time\":1500,\"gameTime\":1400,\"fromPlayer\":2,\"scope\":{\"type\":3,\"id\":1}"));
        Assert.That(json, Does.Contain("\"type\":2,\"time\":9000,\"gameTime\":8000,\"playerId\":2,\"leaveReason\":\"lost_buildings\""));
        Assert.That(json, Does.Not.Contain("to_player_id"));
    }
}
