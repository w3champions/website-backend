using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.MongoDb;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class VersionRepoTest : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSave()
        {
            var versionRepository = new VersionRepository(DbConnctionInfo);

            await versionRepository.SaveLastVersion<IntegrationTestBase>("123");
            var version = await versionRepository.GetLastVersion<IntegrationTestBase>();

            Assert.AreEqual("123", version);
        }

        [Test]
        public async Task SaveTwice()
        {
            var versionRepository = new VersionRepository(DbConnctionInfo);

            await versionRepository.SaveLastVersion<IntegrationTestBase>("123");
            await versionRepository.SaveLastVersion<IntegrationTestBase>("1234");
            var version = await versionRepository.GetLastVersion<IntegrationTestBase>();

            Assert.AreEqual("1234", version);
        }

        [Test]
        public async Task LoadEmpty()
        {
            var versionRepository = new VersionRepository(DbConnctionInfo);

            var version = await versionRepository.GetLastVersion<IntegrationTestBase>();

            Assert.AreEqual("000000000000000000000000", version);
        }
    }
}