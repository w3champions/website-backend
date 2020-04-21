using System.Linq;
using AutoFixture;
using MongoDB.Bson;
using W3ChampionsStatisticService.PadEvents;

namespace WC3ChampionsStatisticService.UnitTests
{
    public static class TestDtoHelper
    {
        public static MatchFinishedEvent CreateFakeEvent()
        {
            var fixture = new Fixture {RepeatCount = 2};
            var fakeEvent = fixture.Build<MatchFinishedEvent>().With(e => e.Id,  ObjectId.GenerateNewId()).Create();

            var name1 = "peter#123";
            var name2 = "wolf#456";

            fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";

            fakeEvent.match.gateway = 10;
            fakeEvent.match.gameMode = 1;

            fakeEvent.match.players.First().battleTag = name1;
            fakeEvent.match.players.First().won = true;
            fakeEvent.match.players.Last().battleTag = name2;
            fakeEvent.match.players.Last().won = false;

            return fakeEvent;
        }

        public static MatchFinishedEvent CreateFake2v2Event()
        {
            var fixture = new Fixture {RepeatCount = 4};
            var fakeEvent = fixture.Build<MatchFinishedEvent>().With(e => e.Id,  ObjectId.GenerateNewId()).Create();

            var name1 = "peter#123";
            var name2 = "wolf#456";
            var name3 = "TEAM2#123";
            var name4 = "TEAM2#456";

            fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";

            fakeEvent.match.gateway = 10;
            fakeEvent.match.gameMode = 2;

            fakeEvent.match.players[0].battleTag = name1;
            fakeEvent.match.players[0].won = true;
            fakeEvent.match.players[1].battleTag = name2;
            fakeEvent.match.players[1].won = true;
            fakeEvent.match.players[2].battleTag = name3;
            fakeEvent.match.players[2].won = false;
            fakeEvent.match.players[3].battleTag = name4;
            fakeEvent.match.players[3].won = false;

            return fakeEvent;
        }

        public static LeagueConstellationChangedEvent CreateFakeLeague()
        {
            var fixture = new Fixture {RepeatCount = 2};
            var fakeEvent = fixture.Build<LeagueConstellationChangedEvent>().Create();

            return fakeEvent;
        }

        public static RankingChangedEvent CreateFakeRankingUpdate()
        {
            var fixture = new Fixture {RepeatCount = 2};
            var fakeEvent = fixture.Build<RankingChangedEvent>().Create();

            return fakeEvent;
        }
    }
}