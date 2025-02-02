using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.PlayerStats.GameLengthForPlayerStatistics;
using W3ChampionsStatisticService.PlayerProfiles;

namespace WC3ChampionsStatisticService.Tests.Statistics;

[TestFixture]
public class PlayerGameLengthStatsTests : IntegrationTestBase
{
    [Test]
    public async Task PlayerGameLengthStats_GamesLengthsAreOK()
    {
        var playerRepo = new PlayerRepository(MongoClient);
        var gameLengthForPlayerStatHandler = new GameLengthForPlayerStatisticsHandler(playerRepo);

        var mfe1 = TestDtoHelper.CreateMatchFinishedEvent("mad#1", "crazy#1", 5, 1699448621000, 1699448821000, Race.HU, Race.NE);
        var mfe2 = TestDtoHelper.CreateMatchFinishedEvent("mad#1", "crazy#2", 5, 1699448631000, 1699448821000, Race.HU, Race.NE);
        var mfe3 = TestDtoHelper.CreateMatchFinishedEvent("mad#1", "crazy#2", 5, 1699448631000, 1699448821000, Race.HU, Race.UD);
        var mfe4 = TestDtoHelper.CreateMatchFinishedEvent("mad#1", "crazy#1", 5, 1699448621000, 1699448821000, Race.HU, Race.UD);

        // short games with less than 120 seconds
        // shouldn't alter the final average
        var mfe5 = TestDtoHelper.CreateMatchFinishedEvent("mad#1", "crazy#1", 5, 1699448621000, 1699448631000, Race.HU, Race.NE);
        var mfe6 = TestDtoHelper.CreateMatchFinishedEvent("mad#1", "crazy#1", 5, 1699448621000, 1699448631000, Race.HU, Race.NE);
        var mfe7 = TestDtoHelper.CreateMatchFinishedEvent("mad#1", "crazy#1", 5, 1699448621000, 1699448631000, Race.HU, Race.NE);

        await gameLengthForPlayerStatHandler.Update(mfe1);
        await gameLengthForPlayerStatHandler.Update(mfe2);
        await gameLengthForPlayerStatHandler.Update(mfe3);
        await gameLengthForPlayerStatHandler.Update(mfe4);
        await gameLengthForPlayerStatHandler.Update(mfe5);
        await gameLengthForPlayerStatHandler.Update(mfe6);
        await gameLengthForPlayerStatHandler.Update(mfe7);

        var gameLengthForPlayerStatistic1 = await playerRepo.LoadGameLengthForPlayerStats("mad#1", 5);

        var sumOpponentRaceLengths =
            gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.NE.ToString("D")].Count +
            gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.UD.ToString("D")].Count;

        // check first element
        Assert.AreEqual(200, gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.Total.ToString("D")][0]);

        // check count of lengths
        Assert.AreEqual(7, gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.Total.ToString("D")].Count);
        Assert.AreEqual(sumOpponentRaceLengths, gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.Total.ToString("D")].Count);
        Assert.AreEqual(5, gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.NE.ToString("D")].Count);
        Assert.AreEqual(2, gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.UD.ToString("D")].Count);

        // check avg
        Assert.AreEqual(195, gameLengthForPlayerStatistic1.AverageGameLengthByOpponentRace[Race.UD.ToString("D")]);
        Assert.AreEqual(195, gameLengthForPlayerStatistic1.AverageGameLengthByOpponentRace[Race.NE.ToString("D")]);
        Assert.AreEqual(195, gameLengthForPlayerStatistic1.AverageGameLengthByOpponentRace[Race.Total.ToString("D")]);

        // should not have key that did not play against
        Assert.False(gameLengthForPlayerStatistic1.AverageGameLengthByOpponentRace.ContainsKey(Race.HU.ToString("D")));
        Assert.False(gameLengthForPlayerStatistic1.GameLengthsByOpponentRace.ContainsKey(Race.HU.ToString("D")));

        // check intervals
        Assert.AreEqual(4, gameLengthForPlayerStatistic1.PlayerGameLengthIntervalByOpponentRace[Race.Total.ToString("D")].Lengths["180"]);
        Assert.AreEqual(2, gameLengthForPlayerStatistic1.PlayerGameLengthIntervalByOpponentRace[Race.NE.ToString("D")].Lengths["180"]);
        Assert.AreEqual(2, gameLengthForPlayerStatistic1.PlayerGameLengthIntervalByOpponentRace[Race.UD.ToString("D")].Lengths["180"]);

        // should not have key that did not play against
        Assert.False(gameLengthForPlayerStatistic1.PlayerGameLengthIntervalByOpponentRace.ContainsKey(Race.HU.ToString("D")));

        // assure generated data for opponent as well
        var gameLengthForPlayerStatistic2 = await playerRepo.LoadGameLengthForPlayerStats("crazy#2", 5);
        Assert.AreEqual(2, gameLengthForPlayerStatistic2.GameLengthsByOpponentRace[Race.Total.ToString("D")].Count);
        Assert.AreEqual(190, gameLengthForPlayerStatistic2.GameLengthsByOpponentRace[Race.Total.ToString("D")][0]);
        Assert.AreEqual(190, gameLengthForPlayerStatistic2.AverageGameLengthByOpponentRace[Race.Total.ToString("D")]);
        Assert.AreEqual(2, gameLengthForPlayerStatistic2.GameLengthsByOpponentRace[Race.HU.ToString("D")].Count);
        Assert.AreEqual(190, gameLengthForPlayerStatistic2.AverageGameLengthByOpponentRace[Race.HU.ToString("D")]);
        Assert.False(gameLengthForPlayerStatistic2.AverageGameLengthByOpponentRace.ContainsKey(Race.UD.ToString("D")));
        Assert.False(gameLengthForPlayerStatistic2.GameLengthsByOpponentRace.ContainsKey(Race.NE.ToString("D")));
    }
}
