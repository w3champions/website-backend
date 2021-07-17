using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
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

            fakeEvent.match.mapName = "amazonia";
            fakeEvent.match.state = 2;
            var mockEvents = new Mock<IMatchEventRepository>();
            mockEvents.SetupSequence(m => m.Load(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new List<MatchFinishedEvent>() { fakeEvent })
                .ReturnsAsync(new List<MatchFinishedEvent>());

            var mockMatchRepo = new Mock<IMatchRepository>();

            var versionRepository = new VersionRepository(MongoClient);

            var handler = new ReadModelHandler<MatchReadModelHandler>(
                mockEvents.Object,
                versionRepository,
                new MatchReadModelHandler(mockMatchRepo.Object));

            await handler.Update();

            mockMatchRepo.Verify(m => m.Insert(It.Is<Matchup>(ma => ma.MapName == "amazonia")), Times.Once);
        }

        [Test]
        public async Task InsertMatchesFail1()
        {
            var fakeEvent = TestDtoHelper.CreateFakeEvent();

            fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";
            fakeEvent.match.state = 3;
            var mockEvents = new Mock<IMatchEventRepository>();
            mockEvents.SetupSequence(m => m.Load(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new List<MatchFinishedEvent>() { fakeEvent })
                .ReturnsAsync(new List<MatchFinishedEvent>());

            var mockMatchRepo = new Mock<IMatchRepository>();

            var versionRepository = new VersionRepository(MongoClient);

            var handler = new ReadModelHandler<MatchReadModelHandler>(
                mockEvents.Object,
                versionRepository,
                new MatchReadModelHandler(mockMatchRepo.Object));

            await handler.Update();

            mockMatchRepo.Verify(m => m.Insert(It.IsAny<Matchup>()), Times.Never);
        }

        [Test]
        public async Task TestThatNewVersionIsUpdated()
        {
             var fakeEvent1 = TestDtoHelper.CreateFakeEvent();
             var fakeEvent2 = TestDtoHelper.CreateFakeEvent();
             var fakeEvent3 = TestDtoHelper.CreateFakeEvent();
             var fakeEvent4 = TestDtoHelper.CreateFakeEvent();
             var fakeEvent5 = TestDtoHelper.CreateFakeEvent();

             fakeEvent1.match.season = 0;
             fakeEvent1.match.startTime = 5000;
             fakeEvent1.Id = ObjectId.GenerateNewId();
             fakeEvent2.match.season = 0;
             fakeEvent2.match.startTime = 4000;
             fakeEvent2.Id = ObjectId.GenerateNewId();
             fakeEvent3.match.season = 1;
             fakeEvent3.match.startTime = 3000;
             fakeEvent3.Id = ObjectId.GenerateNewId();
             fakeEvent4.match.season = 1;
             fakeEvent4.match.startTime = 2000;
             fakeEvent4.match.id = "Test";
             fakeEvent4.Id = ObjectId.GenerateNewId();
             fakeEvent5.match.season = 0;
             fakeEvent5.match.startTime = 1000;
             fakeEvent5.Id = ObjectId.GenerateNewId();

             await InsertMatchEvents(new List<MatchFinishedEvent> { fakeEvent1, fakeEvent2, fakeEvent3, fakeEvent4, fakeEvent5 });

             var matchRepository = new MatchRepository(MongoClient, new OngoingMatchesCache(MongoClient));
             var versionRepository = new VersionRepository(MongoClient);

             var handler = new ReadModelHandler<MatchReadModelHandler>(
                 new MatchEventRepository(MongoClient),
                 versionRepository,
                 new MatchReadModelHandler(matchRepository));

             await handler.Update();

             var version = await versionRepository.GetLastVersion<MatchReadModelHandler>();

             var matches = await matchRepository.Load();

             Assert.AreEqual(1, version.Season);
             Assert.AreEqual(fakeEvent5.Id.ToString(), version.Version);
             Assert.AreEqual(4, matches.Count);
             Assert.AreEqual(fakeEvent4.match.id, matches[0].MatchId);
             Assert.AreEqual(fakeEvent4.Id, matches[0].Id);
        }
    }
}