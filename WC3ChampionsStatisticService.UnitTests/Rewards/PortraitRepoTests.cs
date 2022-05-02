using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Rewards.Portraits;

namespace WC3ChampionsStatisticService.Tests.Rewards
{
    [TestFixture]
    public class PortraitRepoTests : IntegrationTestBase
    {
        [Test]
        public async Task DefineNewPortraits_Success()
        {
            var portraitRepository = new PortraitRepository(MongoClient);
            int[] portraitIds = { 1, 2, 3, 4 };
            string[] portraitGroups = { "bronze", "silver", "gold" };
            await portraitRepository.SaveNewPortraitDefinitions(portraitIds.ToList(), portraitGroups.ToList());

            var portraits = await portraitRepository.LoadPortraitDefinitions();

            Assert.AreEqual(4, portraits.Count);
            Assert.AreEqual(3, portraits.First().Groups.Count());
            Assert.AreEqual(portraits.First().Id, portraitIds.First().ToString());
        }

        [Test]
        public async Task DefineNewPortraits_AlreadyExists_IsNotDuplicated()
        {
            var portraitRepository = new PortraitRepository(MongoClient);
            int[] portraitIds = { 1, 2, 3, 4 };
            List<int> portraitList = portraitIds.ToList();
            await portraitRepository.SaveNewPortraitDefinitions(portraitList);

            portraitList.RemoveAll(x => x > 2);
            await portraitRepository.SaveNewPortraitDefinitions(portraitList);

            var portraits = await portraitRepository.LoadPortraitDefinitions();

            Assert.AreEqual(4, portraits.Count);
        }

        [Test]
        public async Task DeleteDefinedPortrait_Success()
        {
            var portraitRepository = new PortraitRepository(MongoClient);
            int[] portraitIds = { 1, 2, 3, 4 };
            List<int> portraitList = portraitIds.ToList();
            await portraitRepository.SaveNewPortraitDefinitions(portraitList);

            portraitList.RemoveAll(x => x < 3);
            await portraitRepository.DeletePortraitDefinitions(portraitList);

            var portraits = await portraitRepository.LoadPortraitDefinitions();

            Assert.AreEqual(2, portraits.Count);
        }

        [Test]
        public async Task DefineNewPortraits_DuplicateInRequest_IsNotDuplicated()
        {
            var portraitRepository = new PortraitRepository(MongoClient);
            int[] portraitIds = { 1, 1 };
            List<int> portraitList = portraitIds.ToList();
            await portraitRepository.SaveNewPortraitDefinitions(portraitList);

            var portraits = await portraitRepository.LoadPortraitDefinitions();

            Assert.AreEqual(1, portraits.Count);
            Assert.AreEqual(1.ToString(), portraits[0].Id);
        }

        [Test]
        public async Task UpdateDefinedPortrait_HasNoGroups_GroupsAdded_Success()
        {
            // arrange
            var portraitRepository = new PortraitRepository(MongoClient);
            int[] portraitIds = { 1, 2, 3 };
            string[] groupsToAdd = { "bronze", "silver" };
            List<int> portraitList = portraitIds.ToList();
            await portraitRepository.SaveNewPortraitDefinitions(portraitList);

            // act
            await portraitRepository.UpdatePortraitDefinition(
                new List<int>() { portraitIds[0] }, 
                groupsToAdd.ToList());

            // assert
            var portraits = await portraitRepository.LoadPortraitDefinitions();

            Assert.AreEqual(3, portraits.Count);
            Assert.AreEqual(2, portraits[0].Groups.Count);
            Assert.Contains("bronze", portraits[0].Groups);
            Assert.Contains("silver", portraits[0].Groups);
        }

        [Test]
        public async Task UpdateDefinedPortraits_HaveGroupsAlready_GroupsReplacedCorrectly()
        {
            // arrange
            var portraitRepository = new PortraitRepository(MongoClient);
            int[] portraitIds = { 1, 2, 3 };
            string[] startingGroups = { "bronze", "silver" };
            string[] groupsToAdd = { "gold", "platinum" };
            await portraitRepository.SaveNewPortraitDefinitions(
                portraitIds.ToList(), 
                startingGroups.ToList());

            // act
            await portraitRepository.UpdatePortraitDefinition(new List<int>() { portraitIds[0]}, groupsToAdd.ToList());

            // assert
            var portraits = await portraitRepository.LoadPortraitDefinitions();

            Assert.AreEqual(3, portraits.Count);
            Assert.AreEqual(2, portraits[0].Groups.Count);
            Assert.Contains("gold", portraits[0].Groups);
            Assert.Contains("platinum", portraits[0].Groups);
        }

        [Test]
        public async Task DeleteDefinedPortrait_DoesNotExist_NoError()
        {
            var portraitRepository = new PortraitRepository(MongoClient);
            int[] portraitIds = { 1, 2, 3, 4 };
            List<int> portraitList = portraitIds.ToList();
            await portraitRepository.SaveNewPortraitDefinitions(portraitList);

            int[] nonExistentPortraitIds = { 10, 11 };
            List<int> nonExistentPortraitList = nonExistentPortraitIds.ToList();
            await portraitRepository.DeletePortraitDefinitions(nonExistentPortraitList);

            var portraits = await portraitRepository.LoadPortraitDefinitions();

            Assert.AreEqual(4, portraits.Count);
        }
        [Test]
        public async Task LoadDistinctPortraitGroups_ProperlyMapped()
        {
            // arrange
            var portraitRepository = new PortraitRepository(MongoClient);
            await AddTestPortraitDefinitionsWithGroups(portraitRepository);

            //act
            var portraitGroups = await portraitRepository.LoadDistinctPortraitGroups();

            //assert
            Assert.AreEqual(3, portraitGroups.Count());
            Assert.IsTrue(portraitGroups.Select(x => x.Group).Contains("factorOf12"));
            Assert.IsTrue(portraitGroups.Select(x => x.Group).Contains("even"));
            Assert.IsTrue(portraitGroups.Select(x => x.Group).Contains("prime"));
        }

        [Test]
        public async Task GetPortraitGroups_ProperlyMapped()
        {
            // arrange
            var portraitRepository = new PortraitRepository(MongoClient);
            await AddTestPortraitDefinitionsWithGroups(portraitRepository);

            //act
            var allPortraitGroups = await portraitRepository.LoadDistinctPortraitGroups();

            //assert
            var evenGroup = allPortraitGroups.First(x => x.Group == "even");
            var primeGroup = allPortraitGroups.First(x => x.Group == "prime");
            var factorOf12Group = allPortraitGroups.First(x => x.Group == "factorOf12");

            Assert.AreEqual(10, evenGroup.PortraitIds.Count());
            Assert.AreEqual(7, primeGroup.PortraitIds.Count());
            Assert.AreEqual(6, factorOf12Group.PortraitIds.Count());
        }

        private async Task AddTestPortraitDefinitionsWithGroups(PortraitRepository repo)
        {
            for (int i = 1; i <= 20; i++)
            {
                var groups = new List<string>();
                if (12 % i == 0)
                {
                    groups.Add("factorOf12");
                }
                if (i % 2 == 0)
                {
                    groups.Add("even");
                }
                if (i == 2 || i == 3 || i == 5 || i == 7 || i == 11 || i == 13 || i == 17)
                {
                    groups.Add("prime");
                }
                var idDefList = new List<int>();
                idDefList.Add(i);
                await repo.SaveNewPortraitDefinitions(idDefList, groups);
            }
        }
    }
}