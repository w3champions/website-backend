using System;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.CommonValueObjects;

namespace WC3ChampionsStatisticService.Tests
{
    [TestFixture]
    public class PatchTests : IntegrationTestBase
    {
        public class TestData
        {
            public DateTime StartDate { get; set; }
            public string ExpectedResult { get; set; }
        }

        private static TestData[] _testData = new[]
        {
            new TestData(){ StartDate= new DateTime(2020,6,1,0,0,0), ExpectedResult= "1.32.5"},
            new TestData(){ StartDate= new DateTime(2020,6,2,12,0,0), ExpectedResult= "1.32.5"},
            new TestData(){ StartDate= new DateTime(2020,6,2,19,1,0), ExpectedResult= "1.32.6"},
        };

        [Test]
        public async Task TestPatchVersionAgainstMatchStartDate([ValueSource("_testData")] TestData testData)
        {
            var patchRepository = new PatchRepository(MongoClient);
            var patches = TestDtoHelper.CreateFakePatches();
            await patchRepository.InsertPatches(patches);

            Assert.AreEqual(await patchRepository.GetPatchVersionFromDate(testData.StartDate), testData.ExpectedResult);
        }
    }
}