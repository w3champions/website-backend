using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.MongoDb;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class ReadModelHandlerBaseTests : IntegrationTestBase
    {
        [Test]
        public async Task InsertMatches()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();

            fakeEvent.data.mapInfo.name = "Maps/frozenthrone/(4)twistedmeadows.w3x";
            var mockEvents = new Mock<IMatchEventRepository>();
            mockEvents.Setup(m => m.Load(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(new List<MatchFinishedEvent>() { fakeEvent });

            var mockMatchRepo = new Mock<IMatchRepository>();
            var newversion = "newVersion";
            mockMatchRepo.Setup(m => m.Upsert(It.IsAny<List<Matchup>>())).ReturnsAsync(newversion);

            var versionRepository = new Mock<IVersionRepository>();

            var handler = new PopulateReadModelHandlerBase<PopulateMatchReadModelHandler>(
                mockEvents.Object,
                versionRepository.Object,
                new PopulateMatchReadModelHandler(mockMatchRepo.Object));

            await handler.Update();

            versionRepository.Verify(m => m.SaveLastVersion<PopulateMatchReadModelHandler>(newversion));
            mockMatchRepo.Verify(m => m.Upsert(It.Is<List<Matchup>>(ma => ma.Single().Map == "twistedmeadows")), Times.Once);
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

            fakeEvent.data.mapInfo.name = "Maps/frozenthrone/(4)twistedmeadows.w3x";

            var matchup = new Matchup(fakeEvent);

            Assert.AreEqual("twistedmeadows", matchup.Map);
        }
    }
}