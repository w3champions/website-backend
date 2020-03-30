using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerProfiles;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayerTests : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSave()
        {
            var playerRepository = new PlayerRepository(DbConnctionInfo);

            var player = PlayerProfile.Create("peter#123");
            await playerRepository.UpsertPlayer(player);
            var playerLoaded = await playerRepository.Load(player.Id);

            Assert.AreEqual(player.Id, playerLoaded.Id);
        }

        [Test]
        public async Task PlayerMapping()
        {
            var playerRepository = new PlayerRepository(DbConnctionInfo);

            var player = PlayerProfile.Create("peter#123");
            player.RecordWin(Race.HU, GameMode.GM_1v1, true);
            await playerRepository.UpsertPlayer(player);
            var playerLoaded = await playerRepository.Load(player.Id);
            playerLoaded.RecordWin(Race.UD, GameMode.GM_1v1, false);
            await playerRepository.UpsertPlayer(playerLoaded);

            var playerLoadedAgain = await playerRepository.Load(player.Id);

            Assert.AreEqual(player.Id, playerLoaded.Id);
            Assert.AreEqual(player.Id, playerLoadedAgain.Id);
        }

        [Test]
        public async Task PlayerIdMappedRight()
        {
            var playerRepository = new PlayerRepository(DbConnctionInfo);

            var player1 = PlayerProfile.Create("peter#123");
            var player2 = PlayerProfile.Create("wolf#456");

            await playerRepository.UpsertPlayer(player1);
            await playerRepository.UpsertPlayer(player2);

            var playerLoaded = await playerRepository.Load(player2.Id);

            Assert.IsNotNull(playerLoaded);
            Assert.AreEqual(player2.Id, playerLoaded.Id);
        }
    }
}