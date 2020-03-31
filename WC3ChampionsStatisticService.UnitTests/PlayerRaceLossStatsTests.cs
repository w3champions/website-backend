using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerRaceLossRatios;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayerRaceLossStatsTests : IntegrationTestBase
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
        public async Task MapWins()
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
    }
}