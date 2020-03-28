using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.MongoDb;
using W3ChampionsStatisticService.Ports;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class InsertMatchCommandHandlerTests : IntegrationTestBase
    {
        [Test]
        public async Task InsertMatches()
        {
            var fixture = new Fixture();
            var fakeEvent = fixture.Build<MatchFinishedEvent>().Create();
            var mock = new Mock<IMatchEventRepository>();
            mock.Setup(m => m.Load(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(new List<MatchFinishedEvent>() { fakeEvent });

            var matchRepository = new MatchRepository();
            var versionRepository = new VersionRepository();

            var insertMatchCommandHandler = new InsertMatchCommandHandler(
                mock.Object,
                matchRepository,
                versionRepository);

            await insertMatchCommandHandler.Update();
        }

        [Test]
        public async Task MapMatch_Players()
        {
            var fixture = new Fixture{ RepeatCount = 9 };
            var fakeEvent = fixture.Build<MatchFinishedEvent>().Create();

            var name1 = "peter#123";
            var name2 = "wolf#456";

            fakeEvent.data.players.First().battleTag = name1;
            fakeEvent.data.players.First().won = true;
            fakeEvent.data.players.Last().battleTag = name2;
            fakeEvent.data.players.Last().won = false;

            var matchup = new Matchup(fakeEvent);

            Assert.AreEqual("123", matchup.Teams.First().Players.First().BattleTag);
            Assert.AreEqual("peter", matchup.Teams.First().Players.First().Name);

            Assert.AreEqual("456", matchup.Teams.Last().Players.First().BattleTag);
            Assert.AreEqual("wolf", matchup.Teams.Last().Players.First().Name);
        }

        [Test]
        public async Task MapMatch_Map()
        {
            var fixture = new Fixture();
            var fakeEvent = fixture.Build<MatchFinishedEvent>().Create();

            fakeEvent.data.mapInfo.name = "Maps/frozenthrone/(4)twistedmeadows.w3x";

            var matchup = new Matchup(fakeEvent);

            Assert.AreEqual("twistedmeadows", matchup.Map);
        }
    }
}