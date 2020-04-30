using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.PadEvents;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class MatchupEventRepoTests : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSave()
        {
            var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();

            matchFinishedEvent1.match.id = "nmhcCLaRc7";

            await InsertMatchEvent(matchFinishedEvent1);

            var matchEventRepository = new MatchEventRepository(MongoClient);

            await matchEventRepository.InsertIfNotExisting(matchFinishedEvent1);

            var events = await matchEventRepository.Load();

            Assert.AreEqual(1, events.Count);
        }

        [Test]
        public async Task LoadAndSave2()
        {
            var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
            var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();

            matchFinishedEvent1.match.id = "nmhcCLaRc7";
            matchFinishedEvent2.match.id = "ashjkn75j4";

            await InsertMatchEvent(matchFinishedEvent1);

            var matchEventRepository = new MatchEventRepository(MongoClient);

            await matchEventRepository.InsertIfNotExisting(matchFinishedEvent1);
            await matchEventRepository.InsertIfNotExisting(matchFinishedEvent2);

            var events = await matchEventRepository.Load();

            Assert.AreEqual(2, events.Count);
        }

    }
}