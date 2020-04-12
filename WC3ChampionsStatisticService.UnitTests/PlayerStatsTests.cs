using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayerStatsTests : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSaveMapAndRace()
        {
            var playerRepository = new PlayerStatsRepository(MongoClient);

            var player = RaceOnMapVersusRaceRatio.Create("peter#123");
            await playerRepository.UpsertMapAndRaceStat(player);
            var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.Id);

            Assert.AreEqual(player.Id, playerLoaded.Id);
        }

        [Test]
        public async Task MapWinsAndRace()
        {
            var playerRepository = new PlayerStatsRepository(MongoClient);

            var player = RaceOnMapVersusRaceRatio.Create("peter#123");
            player.AddMapWin(Race.HU, Race.UD, "TM", true);
            player.AddMapWin(Race.HU, Race.OC, "EI", true);
            player.AddMapWin(Race.HU, Race.UD, "TM", false);

            await playerRepository.UpsertMapAndRaceStat(player);
            var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.Id);


            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.HU, Race.UD, "TM").Wins);
            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.HU, Race.UD, "TM").Losses);
            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.HU, Race.OC, "EI").Wins);
        }

        [Test]
        public async Task MapWinsAndRaceRnd()
        {
            var playerRepository = new PlayerStatsRepository(MongoClient);

            var player = RaceOnMapVersusRaceRatio.Create("peter#123");
            player.AddMapWin(Race.RnD, Race.UD, "TM", true);
            player.AddMapWin(Race.HU, Race.RnD, "EI", false);

            await playerRepository.UpsertMapAndRaceStat(player);
            var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.Id);

            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.RnD, Race.UD, "TM").Wins);
            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.HU, Race.RnD, "EI").Losses);
        }

        [Test]
        public async Task MapWinsAsTotalRace()
        {
            var playerRepository = new PlayerStatsRepository(MongoClient);

            var player = RaceOnMapVersusRaceRatio.Create("peter#123");
            player.AddMapWin(Race.HU, Race.UD, "TM", true);
            player.AddMapWin(Race.NE, Race.UD, "TM", true);
            player.AddMapWin(Race.OC, Race.UD, "TM", true);

            await playerRepository.UpsertMapAndRaceStat(player);
            var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.Id);

            Assert.AreEqual(3, playerLoaded.GetWinLoss(Race.Total, Race.UD, "TM").Wins);
        }
    }
}