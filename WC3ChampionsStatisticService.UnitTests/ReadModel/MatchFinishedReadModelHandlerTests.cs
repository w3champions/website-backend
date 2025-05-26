using System;
using System.Collections.Generic;
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
    [Test]
    public async Task InsertMatches()
    {
        var fakeEvent = TestDtoHelper.CreateFakeEvent();

        fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";
        fakeEvent.match.state = EMatchState.FINISHED;
        var mockEvents = new Mock<IMatchEventRepository>();
        mockEvents.SetupSequence(m => m.Load<MatchFinishedEvent>(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MatchFinishedEvent>() { fakeEvent })
            .ReturnsAsync(new List<MatchFinishedEvent>());

        var mockMatchRepo = new Mock<IMatchRepository>();
        var mockTrackingService = TestDtoHelper.CreateMockTrackingService();

        var versionRepository = new VersionRepository(MongoClient);

        var handler = new MatchFinishedReadModelHandler<OngoingRemovalMatchFinishedHandler>(
            mockEvents.Object,
            versionRepository,
            new OngoingRemovalMatchFinishedHandler(mockMatchRepo.Object),
            mockTrackingService.Object);

        await handler.Update();

        mockMatchRepo.Verify(m => m.Insert(It.Is<Matchup>(ma => ma.Map == "amazonia")), Times.Once);
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

        var versionRepository = new VersionRepository(MongoClient);

        var handler = new MatchFinishedReadModelHandler<OngoingRemovalMatchFinishedHandler>(
            mockEvents.Object,
            versionRepository,
            new OngoingRemovalMatchFinishedHandler(mockMatchRepo.Object),
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
        var matchRepository = new MatchRepository(MongoClient, new OngoingMatchesCache(MongoClient));
        var versionRepository = new VersionRepository(MongoClient);

        var handler = new MatchFinishedReadModelHandler<OngoingRemovalMatchFinishedHandler>(
            new MatchEventRepository(MongoClient),
            versionRepository,
            new OngoingRemovalMatchFinishedHandler(matchRepository),
            mockTrackingService.Object);

        await handler.Update();

        var version = await versionRepository.GetLastVersion<OngoingRemovalMatchFinishedHandler>();

        var matches = await matchRepository.Load(1, GameMode.GM_1v1);

        Assert.AreEqual(1, version.Season);
        Assert.AreEqual(fakeEvent5.Id.ToString(), version.Version);
        Assert.AreEqual(2, matches.Count);
        Assert.AreEqual(fakeEvent3.match.id, matches[0].MatchId);
        Assert.AreEqual(fakeEvent3.Id, matches[0].Id);
        mockTrackingService.Verify(m => m.TrackException(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
    }
}
