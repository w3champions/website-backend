using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.W3ChampionsStats.GamesPerDay;
using W3ChampionsStatisticService.W3ChampionsStats.RaceAndWinStats;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class W3Stats : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSavePersistsDateTimeInfo()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();

            fakeEvent.match.endTime = 1585701559200;

            var gamesPerDay = new GamesPerDay();
            gamesPerDay.Apply(fakeEvent.match);

            var w3StatsRepo = new W3StatsRepo(DbConnctionInfo);
            await w3StatsRepo.Save(gamesPerDay);

            var gamesReloaded = await w3StatsRepo.LoadGamesPerDay();

            gamesReloaded.Apply(fakeEvent.match);

            Assert.AreEqual(2, gamesReloaded.GameDays.Single().GamesPlayed);
        }
    }
}