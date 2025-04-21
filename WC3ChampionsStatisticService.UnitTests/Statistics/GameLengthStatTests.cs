using System;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.W3ChampionsStats.GameLengths;

namespace WC3ChampionsStatisticService.Tests.Statistics;

[TestFixture]
public class GameLengthStatTests : IntegrationTestBase
{
    [Test]
    public void GameLengthStatsBelow30s()
    {
        var gameLengthStats = GameLengthStat.Create(GameMode.GM_1v1);
        gameLengthStats.Apply(new TimeSpan(0, 0, 20));

        Assert.AreEqual(1, gameLengthStats.Lengths[0].Games);
        Assert.AreEqual(0, gameLengthStats.Lengths[1].Games);
    }

    [Test]
    public void GameLengthStatsLongerThan1hour()
    {
        var gameLengthStats = GameLengthStat.Create(GameMode.GM_1v1);
        gameLengthStats.Apply(new TimeSpan(1, 5, 20));

        Assert.AreEqual(1, gameLengthStats.Lengths[120].Games);
        Assert.AreEqual(3600, gameLengthStats.Lengths[120].Seconds);
    }
}
