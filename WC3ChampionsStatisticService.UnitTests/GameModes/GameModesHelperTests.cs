using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3C.Domain.GameModes;

namespace WC3ChampionsStatisticService.UnitTests.GameModes;

[TestFixture]
public class GameModesHelperTests
{
    // ToArrangedTeamVariant

    [Test]
    public void ToArrangedTeamVariant_2v2_IsAt_Returns2v2AT()
    {
        Assert.AreEqual(GameMode.GM_2v2_AT, GameModesHelper.ToArrangedTeamVariant(GameMode.GM_2v2, true));
    }

    [Test]
    public void ToArrangedTeamVariant_4v4_IsAt_Returns4v4AT()
    {
        Assert.AreEqual(GameMode.GM_4v4_AT, GameModesHelper.ToArrangedTeamVariant(GameMode.GM_4v4, true));
    }

    [Test]
    public void ToArrangedTeamVariant_2v2_NotAt_ReturnsUnchanged()
    {
        Assert.AreEqual(GameMode.GM_2v2, GameModesHelper.ToArrangedTeamVariant(GameMode.GM_2v2, false));
    }

    [Test]
    public void ToArrangedTeamVariant_1v1_IsAt_ReturnsUnchanged()
    {
        // GM_1v1 has no AT variant
        Assert.AreEqual(GameMode.GM_1v1, GameModesHelper.ToArrangedTeamVariant(GameMode.GM_1v1, true));
    }

    [Test]
    public void ToArrangedTeamVariant_Dota5on5_IsAt_ReturnsDota5on5AT()
    {
        Assert.AreEqual(GameMode.GM_DOTA_5ON5_AT, GameModesHelper.ToArrangedTeamVariant(GameMode.GM_DOTA_5ON5, true));
    }

    // IsRaceSplitGameMode

    [Test]
    public void IsRaceSplitGameMode_1v1_ReturnsTrue()
    {
        Assert.IsTrue(GameModesHelper.IsRaceSplitGameMode(GameMode.GM_1v1));
    }

    [Test]
    public void IsRaceSplitGameMode_2v2_ReturnsFalse()
    {
        Assert.IsFalse(GameModesHelper.IsRaceSplitGameMode(GameMode.GM_2v2));
    }

    // UsesRaceInLadderKey

    [Test]
    public void UsesRaceInLadderKey_1v1_Season2_ReturnsTrue()
    {
        Assert.IsTrue(GameModesHelper.UsesRaceInLadderKey(GameMode.GM_1v1, 2));
    }

    [Test]
    public void UsesRaceInLadderKey_1v1_Season3_ReturnsTrue()
    {
        Assert.IsTrue(GameModesHelper.UsesRaceInLadderKey(GameMode.GM_1v1, 3));
    }

    [Test]
    public void UsesRaceInLadderKey_1v1_Season1_ReturnsFalse()
    {
        Assert.IsFalse(GameModesHelper.UsesRaceInLadderKey(GameMode.GM_1v1, 1));
    }

    [Test]
    public void UsesRaceInLadderKey_2v2_Season5_ReturnsFalse()
    {
        Assert.IsFalse(GameModesHelper.UsesRaceInLadderKey(GameMode.GM_2v2, 5));
    }
}
