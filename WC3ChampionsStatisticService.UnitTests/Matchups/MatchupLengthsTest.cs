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
    }
}
