using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class MatchupRepoTests : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSave()
        {
            var matchRepository = new MatchRepository(DbConnctionInfo);

            var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
            var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();

            await matchRepository.Insert(new Matchup(matchFinishedEvent1));
            await matchRepository.Insert(new Matchup(matchFinishedEvent2));
            var matches = await matchRepository.Load();

            Assert.AreEqual(2, matches.Count);
        }

        [Test]
        public async Task LoadAndSearch()
        {
            var matchRepository = new MatchRepository(DbConnctionInfo);

            var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
            var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
            matchFinishedEvent1.match.players[1].battleTag = "Peter#123";

            await matchRepository.Insert(new Matchup(matchFinishedEvent1));
            await matchRepository.Insert(new Matchup(matchFinishedEvent2));

            var matches = await matchRepository.LoadFor("Peter#123");

            Assert.AreEqual(1, matches.Count);
        }

        [Test]
        public async Task LoadAndSearch_InvalidString()
        {
            var matchRepository = new MatchRepository(DbConnctionInfo);

            var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
            var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
            matchFinishedEvent1.match.players[1].battleTag = "Peter#123";

            await matchRepository.Insert(new Matchup(matchFinishedEvent1));
            await matchRepository.Insert(new Matchup(matchFinishedEvent2));

            var matches = await matchRepository.LoadFor("Peter123");

            Assert.AreEqual(0, matches.Count);
        }

        [Test]
        public async Task Upsert()
        {
            var matchRepository = new MatchRepository(DbConnctionInfo);

            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();

            await matchRepository.Insert(new Matchup(matchFinishedEvent));
            await matchRepository.Insert(new Matchup(matchFinishedEvent));
            var matches = await matchRepository.Load();

            Assert.AreEqual(1, matches.Count);
        }
    }
}