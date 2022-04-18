using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats;

namespace WC3ChampionsStatisticService.Tests
{
    [TestFixture]
    public class HeroPlayedStatsTests : IntegrationTestBase
    {
        [Test]
        public async Task HappyPath()
        {
            var w3StatsRepo = new W3StatsRepo(MongoClient);
            var heroPlayedModelHandler = new HeroPlayedStatHandler(w3StatsRepo);

            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();

            matchFinishedEvent.result.players[0].heroes = new List<Hero>
            {
                new Hero { icon = "archmage"},
                new Hero { icon = "mountainking"}
            };
            matchFinishedEvent.result.players[1].heroes = new List<Hero>
            {
                new Hero { icon = "mountainking"}
            };

            await heroPlayedModelHandler.Update(matchFinishedEvent);

            var loadHeroPlayedStat = await w3StatsRepo.LoadHeroPlayedStat();

            // Overall Picks
            Assert.AreEqual(1, loadHeroPlayedStat.Stats[0].OrderedPicks[0].Stats.Single(h => h.Icon == "archmage").Count);
            Assert.AreEqual(2, loadHeroPlayedStat.Stats[0].OrderedPicks[0].Stats.Single(h => h.Icon == "mountainking").Count);
            Assert.AreEqual(1, loadHeroPlayedStat.Stats[0].OrderedPicks[0].Stats[0].Count);

            // First Picks
            Assert.AreEqual(1, loadHeroPlayedStat.Stats[0].OrderedPicks[1].Stats.Single(h => h.Icon == "archmage").Count);
            Assert.AreEqual(1, loadHeroPlayedStat.Stats[0].OrderedPicks[1].Stats.Single(h => h.Icon == "mountainking").Count);

            // Second Picks
            Assert.AreEqual(1, loadHeroPlayedStat.Stats[0].OrderedPicks[2].Stats.Single(h => h.Icon == "mountainking").Count);
        }
    }
}