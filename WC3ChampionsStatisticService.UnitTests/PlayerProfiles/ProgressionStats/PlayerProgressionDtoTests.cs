using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;

namespace WC3ChampionsStatisticService.UnitTests.PlayerProfiles.ProgressionStats;

[TestFixture]
public class PlayerProgressionDtoTests
{
    [Test]
    public void Deserializes_UpdatedProgression_FromInt32_Wire()
    {
        // The upstream serializer writes whole numbers as BSON Int32; these rank values are
        // integer-or-null by construction, so int? is the correct C# type.
        var doc = new BsonDocument
        {
            { "battleTag", "peter#123" },
            { "won", true },
            { "updatedProgression", new BsonDocument
                {
                    { "league", 3 },
                    { "division", 2 },
                    { "points", 50 },
                    { "apexPoints", BsonNull.Value },
                }
            },
        };

        var player = BsonSerializer.Deserialize<PlayerMMrChange>(doc);

        Assert.IsNotNull(player.updatedProgression);
        Assert.AreEqual(3, player.updatedProgression.league);
        Assert.AreEqual(2, player.updatedProgression.division);
        Assert.AreEqual(50, player.updatedProgression.points);
        Assert.IsNull(player.updatedProgression.apexPoints);
    }

    [Test]
    public void UpdatedProgression_Absent_DeserializesToNull()
    {
        var doc = new BsonDocument
        {
            { "battleTag", "peter#123" },
            { "won", false },
        };

        var player = BsonSerializer.Deserialize<PlayerMMrChange>(doc);

        Assert.IsNull(player.updatedProgression);
    }
}
