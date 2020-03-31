using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.PlayerMapAndRaceRatios;
using W3ChampionsStatisticService.PlayerMapRatios;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerRaceLossRatios;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayerStatsTests : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSave()
        {
            var playerRepository = new PlayerRepository(DbConnctionInfo);

            var player = PlayerRaceLossRatio.Create("peter#123");
            await playerRepository.UpsertRaceStat(player);
            var playerLoaded = await playerRepository.LoadRaceStat(player.Id);

            Assert.AreEqual(player.Id, playerLoaded.Id);
        }

        [Test]
        public async Task RaceWins()
        {
            var playerRepository = new PlayerRepository(DbConnctionInfo);

            var player = PlayerRaceLossRatio.Create("peter#123");
            player.AddRaceWin(true, Race.HU, Race.UD);
            player.AddRaceWin(true, Race.HU, Race.UD);
            player.AddRaceWin(true, Race.HU, Race.OC);
            player.AddRaceWin(false, Race.NE, Race.OC);

            await playerRepository.UpsertRaceStat(player);
            var playerLoaded = await playerRepository.LoadRaceStat(player.Id);

            Assert.AreEqual(2, playerLoaded.RaceWinRatio[Race.HU.ToString()][Race.UD.ToString()].Wins);
            Assert.AreEqual(0, playerLoaded.RaceWinRatio[Race.HU.ToString()][Race.UD.ToString()].Losses);
            Assert.AreEqual(1, playerLoaded.RaceWinRatio[Race.HU.ToString()][Race.OC.ToString()].Wins);
            Assert.AreEqual(0, playerLoaded.RaceWinRatio[Race.HU.ToString()][Race.OC.ToString()].Losses);
            Assert.AreEqual(0, playerLoaded.RaceWinRatio[Race.NE.ToString()][Race.OC.ToString()].Wins);
            Assert.AreEqual(1, playerLoaded.RaceWinRatio[Race.NE.ToString()][Race.OC.ToString()].Losses);
        }

        [Test]
        public async Task LoadAndSaveMap()
        {
            var playerRepository = new PlayerRepository(DbConnctionInfo);

            var player = PlayerMapRatio.Create("peter#123");
            await playerRepository.UpsertMapStat(player);
            var playerLoaded = await playerRepository.LoadMapStat(player.Id);

            Assert.AreEqual(player.Id, playerLoaded.Id);
        }

        [Test]
        public async Task MapWins()
        {
            var playerRepository = new PlayerRepository(DbConnctionInfo);

            var player = PlayerMapRatio.Create("peter#123");
            player.AddMapWin(true, Race.HU, "TM");
            player.AddMapWin(true, Race.HU, "EI");
            player.AddMapWin(true, Race.HU, "TM");
            player.AddMapWin(false, Race.NE, "EI");

            await playerRepository.UpsertMapStat(player);
            var playerLoaded = await playerRepository.LoadMapStat(player.Id);

            Assert.AreEqual(2, playerLoaded.RaceWinRatio[Race.HU.ToString()]["TM"].Wins);
            Assert.AreEqual(0, playerLoaded.RaceWinRatio[Race.HU.ToString()]["TM"].Losses);
            Assert.AreEqual(1, playerLoaded.RaceWinRatio[Race.HU.ToString()]["EI"].Wins);
            Assert.AreEqual(0, playerLoaded.RaceWinRatio[Race.HU.ToString()]["EI"].Losses);
            Assert.AreEqual(1, playerLoaded.RaceWinRatio[Race.NE.ToString()]["EI"].Losses);
        }

        [Test]
        public async Task LoadAndSaveMapAndRace()
        {
            var playerRepository = new PlayerRepository(DbConnctionInfo);

            var player = PlayerMapAndRaceRatio.Create("peter#123");
            await playerRepository.UpsertMapAndRaceStat(player);
            var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.Id);

            Assert.AreEqual(player.Id, playerLoaded.Id);
        }

        [Test]
        public async Task MapWinsAndRace()
        {
            var playerRepository = new PlayerRepository(DbConnctionInfo);

            var player = PlayerMapAndRaceRatio.Create("peter#123");
            player.AddMapWin(true, Race.HU, Race.UD, "TM");
            player.AddMapWin(true, Race.HU, Race.OC, "EI");
            player.AddMapWin(false, Race.HU, Race.UD, "TM");

            await playerRepository.UpsertMapAndRaceStat(player);
            var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.Id);

            Assert.AreEqual(1, playerLoaded.RaceWinRatio[Race.HU.ToString()]["TM"][Race.UD.ToString()].Wins);
            Assert.AreEqual(1, playerLoaded.RaceWinRatio[Race.HU.ToString()]["TM"][Race.UD.ToString()].Losses);
            Assert.AreEqual(1, playerLoaded.RaceWinRatio[Race.HU.ToString()]["EI"][Race.OC.ToString()].Wins);
        }
    }
}