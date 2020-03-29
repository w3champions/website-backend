using System;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
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

            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();

            await matchRepository.Insert(new Matchup(matchFinishedEvent));
            await matchRepository.Insert(new Matchup(matchFinishedEvent));
            var matches = await matchRepository.Load(ObjectId.Empty.ToString());

            Assert.AreEqual(2, matches.Count);
        }

        [Test]
        public void MapMatch_Players()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();

            var name1 = "peter#123";
            var name2 = "wolf#456";

            fakeEvent.data.players.First().battleTag = name1;
            fakeEvent.data.players.First().won = false;
            fakeEvent.data.players.Last().battleTag = name2;
            fakeEvent.data.players.Last().won = true;

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
            fakeEvent.data.mapInfo.name = "Twisted Meadows";
            var matchup = new Matchup(fakeEvent);
            Assert.AreEqual("Twisted Meadows", matchup.Map);
        }

        [Test]
        public void MapMatch_TimeSpan()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();
            fakeEvent.data.mapInfo.elapsedGameTimeTotalSeconds = 600;
            var matchup = new Matchup(fakeEvent);
            Assert.AreEqual(new TimeSpan(0, 0, 600), matchup.Duration);
        }

        // [Test]
        // public void MapMatch_StartTime()
        // {
        //     var fakeEvent = TestDtoHelper.CreateFakeEvent();
        //     var matchup = new Matchup(fakeEvent);
        //     Assert.IsNotNull(matchup.StartTime);
        // }
        //
        // [Test]
        // public void MapMatch_GameMode()
        // {
        //     var fakeEvent = TestDtoHelper.CreateFakeEvent();
        //     fakeEvent.data.mapInfo.elapsedGameTimeTotalSeconds = 600;
        //     var matchup = new Matchup(fakeEvent);
        //     Assert.AreEqual(GameMode.GM_1v1, matchup.GameMode);
        // }
    }
}