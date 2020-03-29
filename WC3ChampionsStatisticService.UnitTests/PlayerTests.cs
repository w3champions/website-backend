using System.Threading.Tasks;
using NUnit.Framework;
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

            var player = new Player("peter#123");
            await playerRepository.Upsert(player);
            var playerLoaded = await playerRepository.Load(player.BattleTag);

            Assert.AreEqual(player.BattleTag, playerLoaded.BattleTag);
        }
    }
}