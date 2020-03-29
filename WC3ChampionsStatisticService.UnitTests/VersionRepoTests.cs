using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.ReadModelBase;

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
        public async Task LoadAndSaveTwoVersions()
        {
            var versionRepository = new VersionRepository(DbConnctionInfo);

            await versionRepository.SaveLastVersion<IntegrationTestBase>("123");
            await versionRepository.SaveLastVersion<VersionRepoTest>("456");

            var version1 = await versionRepository.GetLastVersion<IntegrationTestBase>();
            var version2 = await versionRepository.GetLastVersion<VersionRepoTest>();

            Assert.AreEqual("123", version1);
            Assert.AreEqual("456", version2);
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