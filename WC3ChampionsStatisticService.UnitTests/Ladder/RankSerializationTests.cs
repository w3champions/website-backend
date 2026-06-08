using System.Collections.Generic;
using MongoDB.Bson;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace WC3ChampionsStatisticService.UnitTests.Ladder;

[TestFixture]
public class RankSerializationTests
{
    [Test]
    public void Progression_IsNotPersisted_ButSerializesToJsonAsProgression()
    {
        var rank = new Rank(new List<string> { "p#1" }, 1, 12, 1456, null, GateWay.Europe, GameMode.GM_1v1, 2)
        {
            Progression = new PlayerProgressionView { League = 3, Division = 2, Points = 50, ApexPoints = null },
        };

        // BSON: Progression is a serve-time join, never written to the Rank collection.
        var bson = rank.ToBsonDocument();
        Assert.IsFalse(bson.Contains("Progression"), "Progression must be [BsonIgnore]");

        // JSON: serialized for the client (camelCase like the rest of the DTO).
        var json = System.Text.Json.JsonSerializer.Serialize(
            rank,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        StringAssert.Contains("\"progression\"", json);
        StringAssert.Contains("\"league\":3", json);
    }

    [Test]
    public void Progression_NullByDefault()
    {
        var rank = new Rank(new List<string> { "p#1" }, 1, 12, 1456, null, GateWay.Europe, GameMode.GM_1v1, 2);
        Assert.IsNull(rank.Progression);
    }
}
