using System;
using System.Linq;
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
        var mmr = 1500;
        gameLengthStats.Apply(20, mmr, GameMode.GM_1v1);

        Assert.AreEqual(1, gameLengthStats.LengthsByMmrRange["all"][0].Games);
        Assert.AreEqual(0, gameLengthStats.LengthsByMmrRange["all"][1].Games);
    }

    [Test]
    public void GameLengthStatsLongerThan1hour()
    {
        var gameLengthStats = GameLengthStat.Create(GameMode.GM_1v1);
        var duration = (int)new TimeSpan(1, 5, 20).TotalSeconds;
        var mmr = 1500;
        gameLengthStats.Apply(duration, mmr, GameMode.GM_1v1);

        var playedGame = gameLengthStats.LengthsByMmrRange["all"].FindIndex(gl => gl.Games > 0);
        
        Assert.AreEqual(1, gameLengthStats.LengthsByMmrRange["all"][playedGame].Games);
        Assert.AreEqual(3600, gameLengthStats.LengthsByMmrRange["all"][playedGame].Seconds);
    }
}
