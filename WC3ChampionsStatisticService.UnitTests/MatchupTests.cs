using System;
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
            var matchRepository = new MatchRepository(DbConnctionInfo);

            var matchFinishedEvent1 = TestDtoHelper.CreateFakeEvent();
            var matchFinishedEvent2 = TestDtoHelper.CreateFakeEvent();

            await matchRepository.Insert(new Matchup(matchFinishedEvent1));
            await matchRepository.Insert(new Matchup(matchFinishedEvent2));
            var matches = await matchRepository.Load();

            Assert.AreEqual(2, matches.Count);
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

        [Test]
        public void MapMatch_Players()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();

            var name1 = "peter#123";
            var name2 = "wolf#456";

            fakeEvent.match.players.First().battleTag = name1;
            fakeEvent.match.players.First().won = false;
            fakeEvent.match.players.Last().battleTag = name2;
            fakeEvent.match.players.Last().won = true;

            var matchup = new Matchup(fakeEvent);

            Assert.AreEqual("123", matchup.Teams.First().Players.First().BattleTag);
            Assert.AreEqual("peter", matchup.Teams.First().Players.First().Name);

            Assert.AreEqual("456", matchup.Teams.Last().Players.First().BattleTag);
            Assert.AreEqual("wolf", matchup.Teams.Last().Players.First().Name);
        }

        [Test]
        public void MapMatch_Map()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();
            fakeEvent.result.mapInfo.name = "Twisted Meadows";
            var matchup = new Matchup(fakeEvent);
            Assert.AreEqual("Twisted Meadows", matchup.Map);
        }

        [Test]
        public void MapMatch_TimeSpan()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();
            fakeEvent.match.startTime = 600;
            fakeEvent.match.endTime = 1200;
            var matchup = new Matchup(fakeEvent);
            Assert.AreEqual(new TimeSpan(0, 0, 600), matchup.Duration);
        }

        [Test]
        public void MapMatch_StartTime()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();
            var matchup = new Matchup(fakeEvent);
            Assert.IsNotNull(matchup.StartTime);
            Assert.IsNotNull(matchup.EndTime);
        }

        [Test]
        public void MapMatch_GameMode()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();
            fakeEvent.match.gameMode = 1;
            var matchup = new Matchup(fakeEvent);
            Assert.AreEqual(GameMode.GM_1v1, matchup.GameMode);
        }
    }
}