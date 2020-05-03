using System.Threading.Tasks;
using NUnit.Framework;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class OveralHeroStats : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSaveHeroStats()
        {
            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();

        }
    }
}