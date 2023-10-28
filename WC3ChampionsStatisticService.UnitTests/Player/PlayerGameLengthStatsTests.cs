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

        var mfe1 = CreateMatchFinishedEvent("mad#1", "crazy#1", 5, 1699448621000, 1699448626000, Race.HU, Race.NE);
        var mfe2 = CreateMatchFinishedEvent("mad#1", "crazy#2", 5, 1699448621000, 1699448636000, Race.HU, Race.NE);
        var mfe3 = CreateMatchFinishedEvent("mad#1", "crazy#2", 5, 1699448621000, 1699448636000, Race.HU, Race.UD);
        var mfe4 = CreateMatchFinishedEvent("mad#1", "crazy#1", 5, 1699448621000, 1699448626000, Race.HU, Race.UD);

        await gameLengthForPlayerStatHandler.Update(mfe1);
        await gameLengthForPlayerStatHandler.Update(mfe2);
        await gameLengthForPlayerStatHandler.Update(mfe3);
        await gameLengthForPlayerStatHandler.Update(mfe4);

        var gameLengthForPlayerStatistic1 = await playerRepo.LoadGameLengthForPlayerStats("mad#1", 5);

        var sumOpponentRaceLengths =
            gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.NE.ToString("D")].Count 
            + gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.UD.ToString("D")].Count;

        // check first element
        Assert.AreEqual(5, gameLengthForPlayerStatistic1.AllGamesLengths[0]);
        
        // check count of lengths
        Assert.AreEqual(4, gameLengthForPlayerStatistic1.AllGamesLengths.Count);
        Assert.AreEqual(sumOpponentRaceLengths, gameLengthForPlayerStatistic1.AllGamesLengths.Count);
        Assert.AreEqual(2, gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.NE.ToString("D")].Count);
        Assert.AreEqual(2, gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.UD.ToString("D")].Count);
        
        // check avg
        Assert.AreEqual(10, gameLengthForPlayerStatistic1.AverageGameLength);
        Assert.AreEqual(10, gameLengthForPlayerStatistic1.AverageGameLengthByOpponentRace[Race.NE.ToString("D")]);
        Assert.AreEqual(10, gameLengthForPlayerStatistic1.AverageGameLengthByOpponentRace[Race.UD.ToString("D")]);
        
        // should not have key that did not play against
        Assert.False(gameLengthForPlayerStatistic1.AverageGameLengthByOpponentRace.ContainsKey(Race.HU.ToString("D")));
        Assert.False(gameLengthForPlayerStatistic1.GameLengthsByOpponentRace.ContainsKey(Race.HU.ToString("D")));
        
        // check intervals
        Assert.AreEqual(4, gameLengthForPlayerStatistic1.PlayerGameLengthsIntervals.Lengths["0"]);
        Assert.AreEqual(2, gameLengthForPlayerStatistic1.PlayerGameLengthIntervalByOpponentRace[Race.NE.ToString("D")].Lengths["0"]);
        Assert.AreEqual(2, gameLengthForPlayerStatistic1.PlayerGameLengthIntervalByOpponentRace[Race.UD.ToString("D")].Lengths["0"]);

        // should not have key that did not play against
        Assert.False(gameLengthForPlayerStatistic1.PlayerGameLengthIntervalByOpponentRace.ContainsKey(Race.HU.ToString("D")));

        // assure generated data for opponent as well
        var gameLengthForPlayerStatistic2 = await playerRepo.LoadGameLengthForPlayerStats("crazy#2", 5);
        Assert.AreEqual(2, gameLengthForPlayerStatistic2.AllGamesLengths.Count);
        Assert.AreEqual(15, gameLengthForPlayerStatistic2.AllGamesLengths[0]);
        Assert.AreEqual(15, gameLengthForPlayerStatistic2.AverageGameLength);
        Assert.AreEqual(2, gameLengthForPlayerStatistic2.GameLengthsByOpponentRace[Race.HU.ToString("D")].Count);
        Assert.AreEqual(15, gameLengthForPlayerStatistic2.AverageGameLengthByOpponentRace[Race.HU.ToString("D")]);
        Assert.False(gameLengthForPlayerStatistic2.AverageGameLengthByOpponentRace.ContainsKey(Race.UD.ToString("D")));
        Assert.False(gameLengthForPlayerStatistic2.GameLengthsByOpponentRace.ContainsKey(Race.NE.ToString("D")));
    }

    private MatchFinishedEvent CreateMatchFinishedEvent(
        string btag1,
        string btag2,
        int season,
        long startTime,
        long endTime,
        Race race1,
        Race race2)
    {
        var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
        matchFinishedEvent.match.players[0].battleTag = btag1;
        matchFinishedEvent.match.players[1].battleTag = btag2;
        matchFinishedEvent.match.players[0].race = race1;
        matchFinishedEvent.match.players[1].race = race2;
        matchFinishedEvent.match.startTime = startTime;
        matchFinishedEvent.match.endTime = endTime;
        matchFinishedEvent.match.season = season;
        return matchFinishedEvent;
    }
}
