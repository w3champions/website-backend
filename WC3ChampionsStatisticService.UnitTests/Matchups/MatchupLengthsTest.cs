using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.MatchupLengths;

namespace WC3ChampionsStatisticService.Tests.Statistics;

[TestFixture]
public class MatchupLengthsTest : IntegrationTestBase
{
    [Test]
    public async Task MatchupLengthsTest_MatchupLengthsAreOK()
    {
        var statsRepo = new W3StatsRepo(MongoClient);
        var matchupGameLengthHandler = new MatchupLengthsHandler(statsRepo);

        var mfe1 = TestDtoHelper.CreateMatchFinishedEvent("mad#1", "crazy#1", 5, 1699448621000, 1699448826000, Race.HU, Race.NE, 1650, 1550);
        var mfe2 = TestDtoHelper.CreateMatchFinishedEvent("mad#1", "crazy#2", 5, 1699448621000, 1699448836000, Race.HU, Race.NE, 1550, 1350);
        var mfe3 = TestDtoHelper.CreateMatchFinishedEvent("mad#1", "crazy#2", 5, 1699448621000, 1699448836000, Race.HU, Race.UD, 1650, 1550);
        var mfe4 = TestDtoHelper.CreateMatchFinishedEvent("mad#1", "crazy#1", 5, 1699448621000, 1699448826000, Race.HU, Race.UD, 1550, 1350);

        await matchupGameLengthHandler.Update(mfe1);
        await matchupGameLengthHandler.Update(mfe2);
        await matchupGameLengthHandler.Update(mfe3);
        await matchupGameLengthHandler.Update(mfe4);

        var matchupLength = await statsRepo.LoadMatchupLengthOrCreate(Race.HU.ToString(), Race.NE.ToString(), "5");
        var matchupLengthForAllSeasons = await statsRepo.LoadMatchupLengthOrCreate(Race.HU.ToString(), Race.NE.ToString(), "all");


        // Hu vs NE should have 2 games with the key 180
        Assert.True(matchupLength.LengthsByMmrRange.ContainsKey("all"));
        Assert.AreEqual(121, matchupLength.LengthsByMmrRange["all"].Count);
        Assert.AreEqual(1, matchupLength.LengthsByMmrRange["all"][6].Games);
        Assert.AreEqual(1, matchupLength.LengthsByMmrRange["all"][7].Games);
        Assert.AreEqual(180, matchupLength.LengthsByMmrRange["all"][6].Seconds);
        Assert.AreEqual(210, matchupLength.LengthsByMmrRange["all"][7].Seconds);

        // same on mmr 1600
        Assert.True(matchupLength.LengthsByMmrRange.ContainsKey("1600"));
        Assert.True(matchupLength.LengthsByMmrRange.ContainsKey("1400"));
        Assert.AreEqual(121, matchupLength.LengthsByMmrRange["1600"].Count);
        Assert.AreEqual(121, matchupLength.LengthsByMmrRange["1400"].Count);
        Assert.AreEqual(1, matchupLength.LengthsByMmrRange["1600"][6].Games);
        Assert.AreEqual(1, matchupLength.LengthsByMmrRange["1400"][7].Games);
        Assert.AreEqual(180, matchupLength.LengthsByMmrRange["1600"][6].Seconds);
        Assert.AreEqual(210, matchupLength.LengthsByMmrRange["1400"][7].Seconds);


        Assert.True(matchupLengthForAllSeasons.LengthsByMmrRange.ContainsKey("all"));
        Assert.AreEqual(121, matchupLengthForAllSeasons.LengthsByMmrRange["all"].Count);
        Assert.AreEqual(1, matchupLengthForAllSeasons.LengthsByMmrRange["all"][6].Games);
        Assert.AreEqual(1, matchupLengthForAllSeasons.LengthsByMmrRange["all"][7].Games);
        Assert.AreEqual(180, matchupLengthForAllSeasons.LengthsByMmrRange["all"][6].Seconds);
        Assert.AreEqual(210, matchupLengthForAllSeasons.LengthsByMmrRange["all"][7].Seconds);
        

        // var sumOpponentRaceLengths =
        //     gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.NE.ToString("D")].Count 
        //     + gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.UD.ToString("D")].Count;

        // // check first element
        // Assert.AreEqual(5, gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.Total.ToString("D")][0]);
        
        // // check count of lengths
        // Assert.AreEqual(4, gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.Total.ToString("D")].Count);
        // Assert.AreEqual(sumOpponentRaceLengths, gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.Total.ToString("D")].Count);
        // Assert.AreEqual(2, gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.NE.ToString("D")].Count);
        // Assert.AreEqual(2, gameLengthForPlayerStatistic1.GameLengthsByOpponentRace[Race.UD.ToString("D")].Count);
        
        // // check avg
        // Assert.AreEqual(10, gameLengthForPlayerStatistic1.AverageGameLengthByOpponentRace[Race.Total.ToString("D")]);
        // Assert.AreEqual(10, gameLengthForPlayerStatistic1.AverageGameLengthByOpponentRace[Race.NE.ToString("D")]);
        // Assert.AreEqual(10, gameLengthForPlayerStatistic1.AverageGameLengthByOpponentRace[Race.UD.ToString("D")]);
        
        // // should not have key that did not play against
        // Assert.False(gameLengthForPlayerStatistic1.AverageGameLengthByOpponentRace.ContainsKey(Race.HU.ToString("D")));
        // Assert.False(gameLengthForPlayerStatistic1.GameLengthsByOpponentRace.ContainsKey(Race.HU.ToString("D")));
        
        // // check intervals
        // Assert.AreEqual(4, gameLengthForPlayerStatistic1.PlayerGameLengthIntervalByOpponentRace[Race.Total.ToString("D")].Lengths["0"]);
        // Assert.AreEqual(2, gameLengthForPlayerStatistic1.PlayerGameLengthIntervalByOpponentRace[Race.NE.ToString("D")].Lengths["0"]);
        // Assert.AreEqual(2, gameLengthForPlayerStatistic1.PlayerGameLengthIntervalByOpponentRace[Race.UD.ToString("D")].Lengths["0"]);

        // // should not have key that did not play against
        // Assert.False(gameLengthForPlayerStatistic1.PlayerGameLengthIntervalByOpponentRace.ContainsKey(Race.HU.ToString("D")));

        // // assure generated data for opponent as well
        // var gameLengthForPlayerStatistic2 = await playerRepo.LoadGameLengthForPlayerStats("crazy#2", 5);
        // Assert.AreEqual(2, gameLengthForPlayerStatistic2.GameLengthsByOpponentRace[Race.Total.ToString("D")].Count);
        // Assert.AreEqual(15, gameLengthForPlayerStatistic2.GameLengthsByOpponentRace[Race.Total.ToString("D")][0]);
        // Assert.AreEqual(15, gameLengthForPlayerStatistic2.AverageGameLengthByOpponentRace[Race.Total.ToString("D")]);
        // Assert.AreEqual(2, gameLengthForPlayerStatistic2.GameLengthsByOpponentRace[Race.HU.ToString("D")].Count);
        // Assert.AreEqual(15, gameLengthForPlayerStatistic2.AverageGameLengthByOpponentRace[Race.HU.ToString("D")]);
        // Assert.False(gameLengthForPlayerStatistic2.AverageGameLengthByOpponentRace.ContainsKey(Race.UD.ToString("D")));
        // Assert.False(gameLengthForPlayerStatistic2.GameLengthsByOpponentRace.ContainsKey(Race.NE.ToString("D")));
    }
}
