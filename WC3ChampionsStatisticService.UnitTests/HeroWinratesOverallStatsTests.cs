using System.Collections.Generic;
using System.Linq;
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

            var matchFinishedEvent = CreatFakeEvent(new []{"deathknight", "lich"}, new []{ "archmage" });

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

        [Test]
        public async Task HappyPath_MoreGames()
        {
            var w3StatsRepo = new W3StatsRepo(MongoClient);
            var handler = new HeroWinRatePerHeroModelHandler(w3StatsRepo);

            var matchFinishedEvent1 = CreatFakeEvent(new []{"deathknight", "lich"}, new []{ "archmage" });
            var matchFinishedEvent2 = CreatFakeEvent(new []{"lich", }, new []{ "archmage" });
            var matchFinishedEvent3 = CreatFakeEvent(new []{"deathknight", "lich"}, new []{ "moutainking" });
            var matchFinishedEvent4 = CreatFakeEvent(new []{ "moutainking" }, new []{"deathknight", "lich"});

            await handler.Update(matchFinishedEvent1);
            await handler.Update(matchFinishedEvent2);
            await handler.Update(matchFinishedEvent3);
            await handler.Update(matchFinishedEvent4);

            var dkStats = await w3StatsRepo.LoadHeroWinrate("deathknight_lich");
            var lichStats = await w3StatsRepo.LoadHeroWinrate("lich");

            Assert.AreEqual(0, dkStats.WinRates[0].WinLoss.Losses);
            Assert.AreEqual(1, dkStats.WinRates[0].WinLoss.Wins);
            Assert.AreEqual("archmage", dkStats.WinRates[0].HeroCombo);

            Assert.AreEqual(1, dkStats.WinRates[1].WinLoss.Losses);
            Assert.AreEqual(1, dkStats.WinRates[1].WinLoss.Wins);
            Assert.AreEqual("moutainking", dkStats.WinRates[1].HeroCombo);

            Assert.AreEqual(0, lichStats.WinRates[0].WinLoss.Losses);
            Assert.AreEqual(1, lichStats.WinRates[0].WinLoss.Wins);
            Assert.AreEqual("archmage", lichStats.WinRates[0].HeroCombo);
        }

        [Test]
        public async Task HappyPath_MoreGames_DtoHasCorrectSums()
        {
            var w3StatsRepo = new W3StatsRepo(MongoClient);
            var handler = new HeroWinRatePerHeroModelHandler(w3StatsRepo);

            var matchFinishedEvent1 = CreatFakeEvent(new []{"deathknight", "lich"}, new []{ "archmage" });
            var matchFinishedEvent2 = CreatFakeEvent(new []{"lich", }, new []{ "archmage" });
            var matchFinishedEvent3 = CreatFakeEvent(new []{"deathknight", "lich"}, new []{ "archmage", "moutainking" });
            var matchFinishedEvent4 = CreatFakeEvent(new []{ "archmage", "bloodmage" }, new []{"deathknight", "lich"});
            var matchFinishedEvent5 = CreatFakeEvent(new []{ "bloodmage" }, new []{"deathknight", "lich"});

            await handler.Update(matchFinishedEvent1);
            await handler.Update(matchFinishedEvent2);
            await handler.Update(matchFinishedEvent3);
            await handler.Update(matchFinishedEvent4);
            await handler.Update(matchFinishedEvent5);

            var stats = await new W3StatsRepo(MongoClient).LoadHeroWinrateLike("deathknight_lich");
            var heroWinrateDto = new HeroWinrateDto(stats, "archmage", "all", "all");

            Assert.AreEqual(2, heroWinrateDto.Winrate.WinLoss.Wins);
            Assert.AreEqual(1, heroWinrateDto.Winrate.WinLoss.Losses);
        }

        private static MatchFinishedEvent CreatFakeEvent(string[] winnerHeroes, string[] looserHeroes)
        {
            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();
            matchFinishedEvent.match.players[0].battleTag = "peter#123";
            matchFinishedEvent.match.players[0].won = false;
            matchFinishedEvent.match.players[1].battleTag = "wolf#456";
            matchFinishedEvent.match.players[1].won = true;

            matchFinishedEvent.result.players[0].heroes = looserHeroes.Select(h => new Hero { icon = h }).ToList();
            matchFinishedEvent.result.players[1].heroes = winnerHeroes.Select(h => new Hero { icon = h }).ToList();
            matchFinishedEvent.result.players[0].battleTag = "peter#123";
            matchFinishedEvent.result.players[1].battleTag = "wolf#456";
            return matchFinishedEvent;
        }
    }
}