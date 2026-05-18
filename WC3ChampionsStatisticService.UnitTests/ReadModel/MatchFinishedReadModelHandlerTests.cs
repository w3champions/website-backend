using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Domain.Repositories;
using W3C.Contracts.Matchmaking;

namespace WC3ChampionsStatisticService.Tests.ReadModel;

[TestFixture]
public class ReadModelHandlerBaseTests : IntegrationTestBase
{
    [TestCase(true, EMatchState.INIT)]
    [TestCase(false, EMatchState.FINISHED)]
    public async Task InsertMatches(bool wasFakeEvent, EMatchState matchState)
    {
        var fakeEvent = TestDtoHelper.CreateFakeEvent();

        fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";
        fakeEvent.match.state = matchState;
        fakeEvent.WasFakeEvent = wasFakeEvent;
        var mockEvents = new Mock<IMatchEventRepository>();
        mockEvents.SetupSequence(m => m.Load<MatchFinishedEvent>(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync([fakeEvent])
            .ReturnsAsync([]);

        var mockMatchRepo = new Mock<IMatchRepository>();
        var mockTrackingService = TestDtoHelper.CreateMockTrackingService();
        var mockMatchService = TestDtoHelper.CreateMockMatchService(MongoClient);
        var versionRepository = new VersionRepository(MongoClient);

        var handler = new MatchFinishedReadModelHandler<MatchReadModelHandler>(
            mockEvents.Object,
            versionRepository,
            new MatchReadModelHandler(mockMatchRepo.Object, mockMatchService.Object),
            mockTrackingService.Object);

        await handler.Update();

        mockMatchRepo.Verify(m => m.Insert(It.Is<Matchup>(ma => ma.Map == "amazonia")), wasFakeEvent ? Times.Never : Times.Once);
    }

    [Test]
    public void InsertMatchesFail1()
    {
        var fakeEvent = TestDtoHelper.CreateFakeEvent();

        fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";
        fakeEvent.match.state = EMatchState.CANCELED;
        var mockEvents = new Mock<IMatchEventRepository>();
        mockEvents.SetupSequence(m => m.Load<MatchFinishedEvent>(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MatchFinishedEvent>() { fakeEvent })
            .ReturnsAsync(new List<MatchFinishedEvent>());

        var mockMatchRepo = new Mock<IMatchRepository>();
        var mockTrackingService = TestDtoHelper.CreateMockTrackingService();
        var mockMatchService = TestDtoHelper.CreateMockMatchService(MongoClient);
        var versionRepository = new VersionRepository(MongoClient);

        var handler = new MatchFinishedReadModelHandler<MatchReadModelHandler>(
            mockEvents.Object,
            versionRepository,
            new MatchReadModelHandler(mockMatchRepo.Object, mockMatchService.Object),
            mockTrackingService.Object);

        Assert.ThrowsAsync<InvalidOperationException>(() => handler.Update());
        mockMatchRepo.Verify(m => m.Insert(It.IsAny<Matchup>()), Times.Never);
        mockTrackingService.Verify(m => m.TrackException(It.IsAny<InvalidOperationException>(), It.IsAny<string>()), Times.Once);
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
        fakeEvent1.match.endTime = 5500;
        fakeEvent1.match.state = EMatchState.FINISHED;
        fakeEvent1.Id = ObjectId.GenerateNewId();
        fakeEvent2.match.season = 0;
        fakeEvent2.match.startTime = 4000;
        fakeEvent2.match.endTime = 4500;
        fakeEvent2.match.state = EMatchState.FINISHED;
        fakeEvent2.Id = ObjectId.GenerateNewId();
        fakeEvent3.match.season = 1;
        fakeEvent3.match.startTime = 3000;
        fakeEvent3.match.endTime = 3500;
        fakeEvent3.match.state = EMatchState.FINISHED;
        fakeEvent3.Id = ObjectId.GenerateNewId();
        fakeEvent4.match.season = 1;
        fakeEvent4.match.startTime = 2000;
        fakeEvent4.match.endTime = 2500;
        fakeEvent4.match.state = EMatchState.FINISHED;
        fakeEvent4.match.id = "Test";
        fakeEvent4.Id = ObjectId.GenerateNewId();
        fakeEvent5.match.season = 0;
        fakeEvent5.match.startTime = 1000;
        fakeEvent5.match.endTime = 1500;
        fakeEvent5.match.state = EMatchState.FINISHED;
        fakeEvent5.Id = ObjectId.GenerateNewId();

        await InsertMatchEvents(new List<MatchFinishedEvent> { fakeEvent1, fakeEvent2, fakeEvent3, fakeEvent4, fakeEvent5 });

        var mockTrackingService = TestDtoHelper.CreateMockTrackingService();
        var mockTracingService = TestDtoHelper.CreateMockedTracingService();
        var matchRepository = new MatchRepository(MongoClient, new OngoingMatchesCache(MongoClient, mockTracingService.Object));
        var versionRepository = new VersionRepository(MongoClient);
        var mockMatchService = TestDtoHelper.CreateMockMatchService(MongoClient);

        var handler = new MatchFinishedReadModelHandler<MatchReadModelHandler>(
            new MatchEventRepository(MongoClient),
            versionRepository,
            new MatchReadModelHandler(matchRepository, mockMatchService.Object),
            mockTrackingService.Object);

        await handler.Update();

        var version = await versionRepository.GetLastVersion<MatchReadModelHandler>();

        var matches = await matchRepository.Load(1, GameMode.GM_1v1);

        Assert.AreEqual(1, version.Season);
        Assert.AreEqual(fakeEvent5.Id.ToString(), version.Version);
        Assert.AreEqual(2, matches.Count);
        Assert.AreEqual(fakeEvent3.match.id, matches[0].MatchId);
        Assert.AreEqual(fakeEvent3.Id, matches[0].Id);
        mockTrackingService.Verify(m => m.TrackException(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task InnerHandlerReceivesNoAiPlayers()
    {
        var fakeEvent = TestDtoHelper.CreateFakeLegionTdEvent();
        fakeEvent.match.state = EMatchState.FINISHED;
        fakeEvent.WasFakeEvent = false;

        var mockEvents = new Mock<IMatchEventRepository>();
        mockEvents.SetupSequence(m => m.Load<MatchFinishedEvent>(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync([fakeEvent])
            .ReturnsAsync([]);

        var mockTrackingService = TestDtoHelper.CreateMockTrackingService();
        var versionRepository = new VersionRepository(MongoClient);

        MatchFinishedEvent receivedEvent = null;
        var innerHandlerMock = new Mock<IMatchFinishedReadModelHandler>();
        innerHandlerMock
            .Setup(h => h.Update(It.IsAny<MatchFinishedEvent>()))
            .Callback<MatchFinishedEvent>(e => receivedEvent = e)
            .Returns(Task.CompletedTask);

        var handler = new MatchFinishedReadModelHandler<IMatchFinishedReadModelHandler>(
            mockEvents.Object,
            versionRepository,
            innerHandlerMock.Object,
            mockTrackingService.Object);

        await handler.Update();

        Assert.That(receivedEvent, Is.Not.Null);
        Assert.That(receivedEvent.result.players.Any(p => p.isAi), Is.False,
            "Inner handler must not see AI players in result.players");
        Assert.That(receivedEvent.result.players.Any(p => string.IsNullOrEmpty(p.battleTag)), Is.False,
            "Inner handler must not see players with empty battleTag in result.players");
        Assert.That(receivedEvent.match.players.Any(p => string.IsNullOrEmpty(p.battleTag)), Is.False,
            "Inner handler must not see players with empty battleTag in match.players");
        Assert.That(receivedEvent.result.players.Count, Is.EqualTo(2),
            "Human players must be preserved in result.players after stripping");
        Assert.That(receivedEvent.match.players.Count, Is.EqualTo(2),
            "Human players must be preserved in match.players after stripping");
    }
}
