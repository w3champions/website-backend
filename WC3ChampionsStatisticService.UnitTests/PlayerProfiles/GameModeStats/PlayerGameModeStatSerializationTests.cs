using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles.GameModeStats;

[TestFixture]
public class PlayerGameModeStatSerializationTests
{
    [Test]
    public void Progression_IsNotPersisted_ButSerializesToJsonAsProgression()
    {
        var stat = new PlayerGameModeStatPerGateway
        {
            Id = "x",
            Progression = new PlayerProgressionView { League = 3, Division = 2, Points = 50, ApexPoints = null },
        };

        var bson = stat.ToBsonDocument();
        Assert.IsFalse(bson.Contains("Progression"), "Progression must be [BsonIgnore]");

        var json = System.Text.Json.JsonSerializer.Serialize(
            stat,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        StringAssert.Contains("\"progression\"", json);
        StringAssert.Contains("\"league\":3", json);
    }

    [Test]
    public void Progression_NullByDefault()
    {
        var stat = new PlayerGameModeStatPerGateway { Id = "x" };
        Assert.IsNull(stat.Progression);
    }
}
