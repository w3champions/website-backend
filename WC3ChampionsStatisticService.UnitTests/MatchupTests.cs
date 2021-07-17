using System.Linq;
using NUnit.Framework;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Matches;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class MatchupTests
    {
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

            var matchup = Matchup.Create(fakeEvent);

            Assert.AreEqual("wolf#456", matchup.Teams.First().Players.First().BattleTag);
            Assert.AreEqual("wolf#456", matchup.Team1Players);
            Assert.AreEqual("wolf", matchup.Teams.First().Players.First().Name);

            Assert.AreEqual("peter#123", matchup.Teams.Last().Players.First().BattleTag);
            Assert.AreEqual("peter#123", matchup.Team2Players);
            Assert.AreEqual("peter", matchup.Teams.Last().Players.First().Name);
        }

        [Test]
        public void MapMatch_Map()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();
            fakeEvent.match.mapName = "amazonia";
            var matchup = Matchup.Create(fakeEvent);
            Assert.AreEqual("amazonia", matchup.MapName);
        }

        [Test]
        public void MapMatch_TimeSpan()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();
            fakeEvent.match.startTime = 1585692028740;
            fakeEvent.match.endTime = 1585692047363;
            var matchup = Matchup.Create(fakeEvent);
            Assert.AreEqual(0, matchup.Duration.Minutes);
            Assert.AreEqual(18, matchup.Duration.Seconds);
            Assert.AreEqual(623, matchup.Duration.Milliseconds);
        }

        [Test]
        public void MapMatch_MMr()
        {  var fakeEvent = TestDtoHelper.CreateFakeEvent();
            fakeEvent.match.players[0].won = true;
            fakeEvent.match.players[0].mmr.rating = 1437.0358093886573;
            fakeEvent.match.players[0].updatedMmr.rating = 1453.5974731933813;

            fakeEvent.match.players[1].won = false;
            fakeEvent.match.players[1].mmr.rating = 1453.5974731933813;
            fakeEvent.match.players[1].updatedMmr.rating = 1437.0358093886573;
            var matchup = Matchup.Create(fakeEvent);
            Assert.AreEqual(16, matchup.Teams[0].Players[0].MmrGain);
            Assert.AreEqual(1453, matchup.Teams[0].Players[0].CurrentMmr);
            Assert.AreEqual(1437, matchup.Teams[0].Players[0].OldMmr);
            Assert.AreEqual(-16, matchup.Teams[1].Players[0].MmrGain);
            Assert.AreEqual(1437, matchup.Teams[1].Players[0].CurrentMmr);
            Assert.AreEqual(1453, matchup.Teams[1].Players[0].OldMmr);
        }

        [Test]
        public void MapMatch_StartTime()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();
            var matchup = Matchup.Create(fakeEvent);
            fakeEvent.match.startTime = 1585692028740;
            fakeEvent.match.endTime = 1585692047363;
            Assert.IsNotNull(matchup.StartTime);
            Assert.IsNotNull(matchup.EndTime);
        }

        [Test]
        public void MapMatch_GameMode()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();
            fakeEvent.match.gameMode = GameMode.GM_1v1;
            var matchup = Matchup.Create(fakeEvent);
            Assert.AreEqual(GameMode.GM_1v1, matchup.GameMode);
        }
    }
}