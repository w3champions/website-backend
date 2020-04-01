using System.Linq;
using AutoFixture;
using MongoDB.Bson;

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

            fakeEvent.match.players.First().battleTag = name1;
            fakeEvent.match.players.First().won = true;
            fakeEvent.match.players.Last().battleTag = name2;
            fakeEvent.match.players.Last().won = false;

            return fakeEvent;
        }
    }
}