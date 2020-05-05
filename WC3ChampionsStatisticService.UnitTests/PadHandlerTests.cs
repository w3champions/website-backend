using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents.PadSync;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PadHandlerTests : IntegrationTestBase
    {
        [Test]
        [Ignore("Just for manual tests")]
        public async Task LoadAndSave()
        {
            var padServiceRepo = new PadServiceRepo();
            var events = await padServiceRepo.GetFrom(0);

            Assert.AreEqual(100, events.Count);
        }
    }
}