using System.Text.Json;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;

namespace WC3ChampionsStatisticService.Tests.Maps;

// Regression: the map editor sends forces as `player_set` (snake_case, as matchmaking and
// update-service spell it). [FromBody] binds with System.Text.Json, which ignored the
// Newtonsoft-only attribute and stored player_set = 0 in matchmaking on every map save.
[TestFixture]
public class GameMapForceJsonTests
{
    // Mirrors the JsonSerializerOptions ASP.NET Core uses for [FromBody] binding and responses.
    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    private const string MapPayload = """
    {
      "id": 83,
      "name": "Seek and Destroy",
      "gameMap": {
        "sha1": "5074a7752a342caf59f3e12ac55da732072a1d71",
        "forces": [{ "name": "Force 1", "flags": 0, "player_set": 4294967295 }]
      }
    }
    """;

    [Test]
    public void Map_body_binding_keeps_the_force_player_set()
    {
        var map = JsonSerializer.Deserialize<MapContract>(MapPayload, WebDefaults);

        Assert.That(map.GameMap.Forces[0].PlayerSet, Is.EqualTo(4294967295L));
    }

    [Test]
    public void Map_response_spells_player_set_snake_case()
    {
        var force = new GameMapForce { Name = "Force 1", Flags = 0, PlayerSet = 4294967295L };

        var json = JsonSerializer.Serialize(force, WebDefaults);

        Assert.That(json, Does.Contain("\"player_set\":4294967295"));
    }

    [Test]
    public void Matchmaking_request_spells_player_set_snake_case()
    {
        var force = new GameMapForce { Name = "Force 1", Flags = 0, PlayerSet = 4294967295L };

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(force);

        Assert.That(json, Does.Contain("\"player_set\":4294967295"));
    }
}
