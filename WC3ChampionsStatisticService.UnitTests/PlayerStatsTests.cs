using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.PlayerStats.RaceVersusRaceStats;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayerStatsTests : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSave()
        {
            var playerRepository = new PlayerStatsRepository(DbConnctionInfo);

            var player = RaceVersusRaceRatio.Create("peter#123");
            await playerRepository.UpsertRaceStat(player);
            var playerLoaded = await playerRepository.LoadRaceStat(player.Id);

            Assert.AreEqual(player.Id, playerLoaded.Id);
        }

        [Test]
        public async Task RaceWinsRnd()
        {
            var playerRepository = new PlayerStatsRepository(DbConnctionInfo);

            var player = RaceVersusRaceRatio.Create("peter#123");
            player.AddRaceWin(true, Race.RnD, Race.UD);
            player.AddRaceWin(false, Race.HU, Race.RnD);

            await playerRepository.UpsertRaceStat(player);
            var playerLoaded = await playerRepository.LoadRaceStat(player.Id);


            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.RnD, Race.UD).Wins);
            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.HU, Race.RnD).Losses);
        }

        [Test]
        public async Task RaceWins()
        {
            var playerRepository = new PlayerStatsRepository(DbConnctionInfo);

            var player = RaceVersusRaceRatio.Create("peter#123");
            player.AddRaceWin(true, Race.HU, Race.UD);
            player.AddRaceWin(true, Race.HU, Race.UD);
            player.AddRaceWin(true, Race.HU, Race.OC);
            player.AddRaceWin(false, Race.NE, Race.OC);

            await playerRepository.UpsertRaceStat(player);
            var playerLoaded = await playerRepository.LoadRaceStat(player.Id);

            Assert.AreEqual(2, playerLoaded.GetWinLoss(Race.HU, Race.UD).Wins);
            Assert.AreEqual(0, playerLoaded.GetWinLoss(Race.HU, Race.UD).Losses);
            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.HU, Race.OC).Wins);
            Assert.AreEqual(0, playerLoaded.GetWinLoss(Race.HU, Race.OC).Losses);
            Assert.AreEqual(0, playerLoaded.GetWinLoss(Race.NE, Race.OC).Wins);
            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.NE, Race.OC).Losses);
        }

        [Test]
        public async Task LoadAndSaveMap()
        {
            var playerRepository = new PlayerStatsRepository(DbConnctionInfo);

            var player = RaceOnMapRatio.Create("peter#123");
            await playerRepository.UpsertMapStat(player);
            var playerLoaded = await playerRepository.LoadMapStat(player.Id);

            Assert.AreEqual(player.Id, playerLoaded.Id);
        }

        [Test]
        public async Task MapWins()
        {
            var playerRepository = new PlayerStatsRepository(DbConnctionInfo);

            var player = RaceOnMapRatio.Create("peter#123");
            player.AddMapWin(Race.HU, "TM", true);
            player.AddMapWin(Race.HU, "EI", true);
            player.AddMapWin(Race.HU, "TM", true);
            player.AddMapWin(Race.NE, "EI", false);

            await playerRepository.UpsertMapStat(player);
            var playerLoaded = await playerRepository.LoadMapStat(player.Id);

            Assert.AreEqual(2, playerLoaded.GetWinLoss(Race.HU, "TM").Wins);
            Assert.AreEqual(0, playerLoaded.GetWinLoss(Race.HU, "TM").Losses);
            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.HU, "EI").Wins);
            Assert.AreEqual(0, playerLoaded.GetWinLoss(Race.HU, "EI").Losses);
            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.NE, "EI").Losses);
        }

        [Test]
        public async Task MapWinsRnd()
        {
            var playerRepository = new PlayerStatsRepository(DbConnctionInfo);

            var player = RaceOnMapRatio.Create("peter#123");
            player.AddMapWin(Race.RnD, "TM", true);

            await playerRepository.UpsertMapStat(player);
            var playerLoaded = await playerRepository.LoadMapStat(player.Id);

            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.RnD, "TM").Wins);
        }

        [Test]
        public async Task LoadAndSaveMapAndRace()
        {
            var playerRepository = new PlayerStatsRepository(DbConnctionInfo);

            var player = RaceOnMapVersusRaceRatio.Create("peter#123");
            await playerRepository.UpsertMapAndRaceStat(player);
            var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.Id);

            Assert.AreEqual(player.Id, playerLoaded.Id);
        }

        [Test]
        public async Task MapWinsAndRace()
        {
            var playerRepository = new PlayerStatsRepository(DbConnctionInfo);

            var player = RaceOnMapVersusRaceRatio.Create("peter#123");
            player.AddMapWin(true, Race.HU, Race.UD, "TM");
            player.AddMapWin(true, Race.HU, Race.OC, "EI");
            player.AddMapWin(false, Race.HU, Race.UD, "TM");

            await playerRepository.UpsertMapAndRaceStat(player);
            var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.Id);

            Assert.AreEqual(1, playerLoaded.RaceWinRatio[Race.HU.ToString()]["TM"][Race.UD.ToString()].Wins);
            Assert.AreEqual(1, playerLoaded.RaceWinRatio[Race.HU.ToString()]["TM"][Race.UD.ToString()].Losses);
            Assert.AreEqual(1, playerLoaded.RaceWinRatio[Race.HU.ToString()]["EI"][Race.OC.ToString()].Wins);
        }

        [Test]
        public async Task MapWinsAndRaceRnd()
        {
            var playerRepository = new PlayerStatsRepository(DbConnctionInfo);

            var player = RaceOnMapVersusRaceRatio.Create("peter#123");
            player.AddMapWin(true, Race.RnD, Race.UD, "TM");
            player.AddMapWin(false, Race.HU, Race.RnD, "EI");

            await playerRepository.UpsertMapAndRaceStat(player);
            var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.Id);

            Assert.AreEqual(1, playerLoaded.RaceWinRatio[Race.RnD.ToString()]["TM"][Race.UD.ToString()].Wins);
            Assert.AreEqual(1, playerLoaded.RaceWinRatio[Race.HU.ToString()]["EI"][Race.RnD.ToString()].Losses);
        }
    }
}