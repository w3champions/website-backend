using System.Threading.Tasks;
using NUnit.Framework;
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