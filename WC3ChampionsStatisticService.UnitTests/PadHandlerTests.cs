using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PadEvents.PadSync;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PadHandlerTests : IntegrationTestBase
    {
        [Test]
        [Ignore("Just for manual tests")]
        public async Task GetGames()
        {
            var padServiceRepo = new PadServiceRepo();
            var events = await padServiceRepo.GetFrom(0);

            Assert.AreEqual(100, events.Count);
        }

        [Test]
        [Ignore("Just for manual tests")]
        public async Task GetPlayer()
        {
            var padServiceRepo = new PadServiceRepo();
            var player = await padServiceRepo.GetPlayer("ToD#2792");

            Assert.IsNotNull(player);
            Assert.AreNotEqual(0, player.data.ladder["20"].solo.wins);
            Assert.AreNotEqual(0, player.data.ladder["20"].solo.losses);
        }

        [Test]
        [Ignore("Just for manual tests")]
        public async Task GeLeagues()
        {
            var padServiceRepo = new PadServiceRepo();
            var league = await padServiceRepo.GetLeague(GateWay.Europe, GameMode.GM_1v1);

            Assert.IsNotNull(league);
            Assert.AreEqual(0, league.Leagues[0].Order);
            Assert.AreEqual("Grand Master League", league.Leagues[0].Name);
            Assert.AreEqual(3, league.Leagues[4].Division);
        }

        [Test]
        [Ignore("Just for manual tests")]
        public async Task GhetPlayer_Null()
        {
            var padServiceRepo = new PadServiceRepo();
            var player = await padServiceRepo.GetPlayer("Tosdfsdfsdfas3wadfD#2792");

            Assert.IsNull(player);
        }
    }
}