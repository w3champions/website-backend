using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.PlayerOverviews;
using W3ChampionsStatisticService.PlayerProfiles;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayerOverviewTests : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSave()
        {
            var playerRepository = new PlayerRepository(DbConnctionInfo);

            var player = new PlayerOverview("peter#123", 20);
            await playerRepository.UpsertPlayer(player);
            var playerLoaded = await playerRepository.LoadOverview(player.Id);

            Assert.AreEqual(player.Id, playerLoaded.Id);
            Assert.AreEqual(20, playerLoaded.GateWay);
        }

        [Test]
        public async Task LoadAndSaveSince()
        {
            var playerRepository = new PlayerRepository(DbConnctionInfo);

            var player1 = new PlayerOverview("peter#123", 1);
            player1.MMR = 15;
            var player2 = new PlayerOverview("peter#1234", 1);
            player1.MMR = 17;
            var player3 = new PlayerOverview("peter#12345", 1);
            player1.MMR = 19;
            await playerRepository.UpsertPlayer(player1);
            await playerRepository.UpsertPlayer(player2);
            await playerRepository.UpsertPlayer(player3);
            var playerLoaded = await playerRepository.LoadOverviewSince(15, 1, 1);

            Assert.AreEqual(player2.Id, playerLoaded.Single().Id);
        }

        [Test]
        public void UpdateOverview()
        {
            var player = new PlayerOverview("peter#123", 1);
            player.RecordWin(true, 1230);
            player.RecordWin(false, 1240);
            player.RecordWin(false, 1250);

            Assert.AreEqual(3, player.Games);
            Assert.AreEqual(1, player.TotalWins);
            Assert.AreEqual(2, player.TotalLosses);
            Assert.AreEqual("123", player.BattleTag);
            Assert.AreEqual("peter", player.Name);
            Assert.AreEqual("peter#123", player.Id);
            Assert.AreEqual(1250, player.MMR);
        }
    }
}