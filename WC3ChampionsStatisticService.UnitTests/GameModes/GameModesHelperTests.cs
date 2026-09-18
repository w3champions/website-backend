using Newtonsoft.Json;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3C.Domain.GameModes;
using W3C.Domain.MatchmakingService;

namespace WC3ChampionsStatisticService.Tests.GameModes;

[TestFixture]
public class GameModesHelperTests
{
    [Test]
    public void FootmenFrenzyIsAnFfaGameMode()
    {
        Assert.That(GameModesHelper.IsFfaGameMode(GameMode.GM_FOOTMEN_FRENZY), Is.True);
    }

    [Test]
    public void TheEstablishedFfaGameModesAreStillFfa()
    {
        Assert.That(GameModesHelper.IsFfaGameMode(GameMode.FFA), Is.True);
        Assert.That(GameModesHelper.IsFfaGameMode(GameMode.GM_SC_FFA_4), Is.True);
        Assert.That(GameModesHelper.IsFfaGameMode(GameMode.GM_SC_OZ), Is.True);
    }

    [Test]
    public void LtwFfaIsNotAnonymisedAndStaysOutOfTheFfaList()
    {
        Assert.That(GameModesHelper.IsFfaGameMode(GameMode.GM_LTW_FFA), Is.False);
        Assert.That(GameModesHelper.IsFfaGameMode(GameMode.GM_1v1), Is.False);
    }
}

[TestFixture]
public class MatchJsonMappingTests
{
    private const string CanceledMatchJson = """
    {
      "_id": "abc1234567",
      "_created_at": "2026-08-01T10:00:00.000Z",
      "_updated_at": "2026-08-01T16:05:00.000Z",
      "state": 3,
      "gameMode": 5,
      "gamename": "w3c-test",
      "startTime": 1754042400000,
      "floGameId": 4242,
      "players": [
        { "battleTag": "Tester#1234", "team": 0, "race": 1, "slotIndex": 2 }
      ]
    }
    """;

    [Test]
    public void MatchIdIsReadFromTheUnderscoreIdJsonProperty()
    {
        var match = JsonConvert.DeserializeObject<Match>(CanceledMatchJson);

        Assert.That(match.id, Is.EqualTo("abc1234567"));
    }

    [Test]
    public void EntityTimestampsAreMapped()
    {
        var match = JsonConvert.DeserializeObject<Match>(CanceledMatchJson);

        Assert.That(match.createdAt, Is.Not.Null);
        Assert.That(match.canceledAt, Is.Not.Null);
        Assert.That(match.canceledAt!.Value, Is.GreaterThan(match.createdAt!.Value));
    }

    [Test]
    public void SlotIndexIsMappedOnPlayers()
    {
        var match = JsonConvert.DeserializeObject<Match>(CanceledMatchJson);

        Assert.That(match.players[0].slotIndex, Is.EqualTo(2));
    }

    [Test]
    public void AMissingEndTimeStaysZeroRatherThanThrowing()
    {
        var match = JsonConvert.DeserializeObject<Match>(CanceledMatchJson);

        Assert.That(match.endTime, Is.EqualTo(0));
    }

    [Test]
    public void AMissingFloGameIdDeserialisesToNull()
    {
        var match = JsonConvert.DeserializeObject<Match>("""{"_id":"x","state":3}""");

        Assert.That(match.floGameId, Is.Null);
        Assert.That(match.HasFloGameId(), Is.False);
    }
}
