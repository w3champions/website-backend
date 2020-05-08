using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.HeroWinrate;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class HeroWinratesOverallStatsTests : IntegrationTestBase
    {
        [Test]
        public async Task HappyPath()
        {
            var w3StatsRepo = new W3StatsRepo(MongoClient);
            var handler = new HeroWinRatePerHeroModelHandler(w3StatsRepo);

            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
            matchFinishedEvent.match.players[0].battleTag = "peter#123";
            matchFinishedEvent.match.players[0].won = false;
            matchFinishedEvent.match.players[1].battleTag = "wolf#456";
            matchFinishedEvent.match.players[1].won = true;

            matchFinishedEvent.result.players[0].heroes = new List<Hero> { new Hero { icon = "archmage"}};
            matchFinishedEvent.result.players[1].heroes = new List<Hero> { new Hero { icon = "deathknight"}, new Hero { icon = "lich"}};
            matchFinishedEvent.result.players[0].battleTag = "peter#123";
            matchFinishedEvent.result.players[1].battleTag = "wolf#456";

            await handler.Update(matchFinishedEvent);

            var amStats = await w3StatsRepo.LoadHeroWinrate("archmage");
            var dkStats = await w3StatsRepo.LoadHeroWinrate("deathknight_lich");

            Assert.AreEqual(1, amStats.WinRates[0].WinLoss.Losses);
            Assert.AreEqual(0, amStats.WinRates[0].WinLoss.Wins);
            Assert.AreEqual("deathknight_lich", amStats.WinRates[0].HeroCombo);

            Assert.AreEqual(0, dkStats.WinRates[0].WinLoss.Losses);
            Assert.AreEqual(1, dkStats.WinRates[0].WinLoss.Wins);
            Assert.AreEqual("archmage", dkStats.WinRates[0].HeroCombo);
        }
    }
}