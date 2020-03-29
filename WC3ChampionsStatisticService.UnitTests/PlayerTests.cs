using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Players;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayerTests : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSave()
        {
            var playerRepository = new PlayerRepository(DbConnctionInfo);

            var player = PlayerFactory.Create("peter#123");
            await playerRepository.Upsert(player);
            var playerLoaded = await playerRepository.Load(player.BattleTag);

            Assert.AreEqual(player.BattleTag, playerLoaded.BattleTag);
        }

        [Test]
        public async Task PlayerMapping()
        {
            var playerRepository = new PlayerRepository(DbConnctionInfo);

            var player = PlayerFactory.Create("peter#123");
            player.RecordWin(Race.HU, GameMode.GM_1v1, true);
            await playerRepository.Upsert(player);
            var playerLoaded = await playerRepository.Load(player.BattleTag);
            playerLoaded.RecordWin(Race.UD, GameMode.GM_1v1, false);
            await playerRepository.Upsert(playerLoaded);

            var playerLoadedAgain = await playerRepository.Load(player.BattleTag);

            Assert.AreEqual(player.BattleTag, playerLoaded.BattleTag);
            Assert.AreEqual(player.BattleTag, playerLoadedAgain.BattleTag);
        }
    }
}