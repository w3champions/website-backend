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

            var player = new PlayerOverview("peter#123");
            await playerRepository.UpsertPlayer(player);
            var playerLoaded = await playerRepository.LoadOverview(player.Id);

            Assert.AreEqual(player.Id, playerLoaded.Id);
        }

        [Test]
        public void UpdateOverview()
        {
            var player = new PlayerOverview("peter#123");
            player.RecordWin(true);
            player.RecordWin(false);
            player.RecordWin(false);

            Assert.AreEqual(3, player.Games);
            Assert.AreEqual(1, player.TotalWins);
            Assert.AreEqual(2, player.TotalLosses);
            Assert.AreEqual("123", player.BattleTag);
            Assert.AreEqual("peter", player.Name);
            Assert.AreEqual("peter#123", player.Id);
            Assert.AreEqual(0, player.MMR);
        }
    }
}