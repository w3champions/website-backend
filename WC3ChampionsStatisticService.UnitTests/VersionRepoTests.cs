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
            var versionRepository = new VersionRepository(MongoClient);

            await versionRepository.SaveLastVersion<IntegrationTestBase>("123", false);
            var version = await versionRepository.GetLastVersion<IntegrationTestBase>(false);

            Assert.AreEqual("123", version.Version);
        }

        [Test]
        public async Task LoadAndSaveTwoVersions()
        {
            var versionRepository = new VersionRepository(MongoClient);

            await versionRepository.SaveLastVersion<IntegrationTestBase>("123", false);
            await versionRepository.SaveLastVersion<VersionRepoTest>("456", false);

            var version1 = await versionRepository.GetLastVersion<IntegrationTestBase>(false);
            var version2 = await versionRepository.GetLastVersion<VersionRepoTest>(false);

            Assert.AreEqual("123", version1.Version);
            Assert.AreEqual("456", version2.Version);
        }

        [Test]
        public async Task SaveTwice()
        {
            var versionRepository = new VersionRepository(MongoClient);

            await versionRepository.SaveLastVersion<IntegrationTestBase>("123", false);
            await versionRepository.SaveLastVersion<IntegrationTestBase>("1234", false);
            var version = await versionRepository.GetLastVersion<IntegrationTestBase>(false);

            Assert.AreEqual("1234", version.Version);
        }

        [Test]
        public async Task LoadEmpty()
        {
            var versionRepository = new VersionRepository(MongoClient);

            var version = await versionRepository.GetLastVersion<IntegrationTestBase>(false);

            Assert.AreEqual("000000000000000000000000", version.Version);
        }
    }
}