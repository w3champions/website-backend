using System.Linq;
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
            var matchRepository = new MatchRepository(MongoClient);

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
            var matchRepository = new MatchRepository(MongoClient);

            var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
            var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
            matchFinishedEvent1.match.players[1].id = "peter#123@10";
            matchFinishedEvent1.match.players[1].won = true;
            matchFinishedEvent1.match.players[0].won = false;
            matchFinishedEvent1.match.gateway = 10;
            matchFinishedEvent2.match.gateway = 10;

            await matchRepository.Insert(new Matchup(matchFinishedEvent1));
            await matchRepository.Insert(new Matchup(matchFinishedEvent2));

            var matches = await matchRepository.LoadFor("peter#123@10");

            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual("peter#123@10", matches[0].Teams[0].Players[0].Id);
        }

        [Test]
        public async Task LoadAndSearch_InvalidString()
        {
            var matchRepository = new MatchRepository(MongoClient);

            var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
            var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
            matchFinishedEvent1.match.players[1].id = "peter#123@10";

            await matchRepository.Insert(new Matchup(matchFinishedEvent1));
            await matchRepository.Insert(new Matchup(matchFinishedEvent2));

            var matches = await matchRepository.LoadFor("eter#123@10");

            Assert.AreEqual(0, matches.Count);
        }

        [Test]
        public async Task Upsert()
        {
            var matchRepository = new MatchRepository(MongoClient);

            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();

            await matchRepository.Insert(new Matchup(matchFinishedEvent));
            await matchRepository.Insert(new Matchup(matchFinishedEvent));
            var matches = await matchRepository.Load();

            Assert.AreEqual(1, matches.Count);
        }

        [Test]
        public async Task CountFor()
        {
            var matchRepository = new MatchRepository(MongoClient);

            var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
            var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();
            var matchFinishedEvent3 = TestDtoHelper.CreateFakeEvent();

            matchFinishedEvent1.match.players[0].id = "peter#123@10";
            matchFinishedEvent1.match.players[1].id = "wolf#456@10";

            matchFinishedEvent2.match.players[0].id = "wolf#456@10";
            matchFinishedEvent2.match.players[1].id = "peter#123@10";

            matchFinishedEvent3.match.players[0].id = "notFound";
            matchFinishedEvent3.match.players[1].id = "notFound2";

            var matchup = new Matchup(matchFinishedEvent1);
            await matchRepository.Insert(matchup);
            await matchRepository.Insert(new Matchup(matchFinishedEvent2));
            var count = await matchRepository.CountFor(matchup.Teams[0].Players[0].Id);

            Assert.AreEqual(2, count);
        }

        [Test]
        public async Task SearchForPlayerAndOpponent()
        {
            var matchRepository = new MatchRepository(MongoClient);

            var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
            var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();

            matchFinishedEvent1.match.players[0].id = "peter#123@10";
            matchFinishedEvent1.match.players[1].id = "wolf#456@10";

            matchFinishedEvent2.match.players[0].id = "peter#123@10";
            matchFinishedEvent2.match.players[1].id = "ANDERER#456@10";

            await matchRepository.Insert(new Matchup(matchFinishedEvent1));
            await matchRepository.Insert(new Matchup(matchFinishedEvent2));
            var matches = await matchRepository.LoadFor("peter#123@10", "wolf#456@10");
            var count = await matchRepository.CountFor("peter#123@10", "wolf#456@10");

            Assert.AreEqual(1, count);
            Assert.AreEqual("peter#123@10", matches.Single().Teams.First().Players.Single().Id);
            Assert.AreEqual("wolf#456@10", matches.Single().Teams.First().Players.Single().Id);
        }
    }
}