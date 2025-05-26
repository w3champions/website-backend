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
using System;

namespace WC3ChampionsStatisticService.Tests.ReadModel;

[TestFixture]
public class MatchCanceledReadModelHandlerTests : IntegrationTestBase
{
    [Test]
    public async Task DeleteCanceledMatch()
    {
        var fakeEvent = TestDtoHelper.CreateFakeMatchCanceledEvent();
        var mockEvents = new Mock<IMatchEventRepository>();
        mockEvents.SetupSequence(m => m.Load<MatchCanceledEvent>(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MatchCanceledEvent>() { fakeEvent })
            .ReturnsAsync(new List<MatchCanceledEvent>());

        var mockMatchRepo = new Mock<IMatchRepository>();
        mockMatchRepo.Setup(m => m.LoadDetailsByOngoingMatchId(It.IsAny<string>())).ReturnsAsync(new MatchupDetail());

        var versionRepository = new VersionRepository(MongoClient);
        var trackingService = TestDtoHelper.CreateMockTrackingService();

        var handler = new MatchCanceledReadModelHandler<OngoingRemovalMatchCanceledHandler>(
            mockEvents.Object,
            versionRepository,
            new OngoingRemovalMatchCanceledHandler(mockMatchRepo.Object),
            trackingService.Object);

        await handler.Update();

        mockMatchRepo.Verify(m => m.DeleteOnGoingMatch(It.IsAny<Matchup>()), Times.Once);
    }

    [Test]
    public async Task CanceledMatchNotFound()
    {
        var fakeEvent = TestDtoHelper.CreateFakeMatchCanceledEvent();
        var mockEvents = new Mock<IMatchEventRepository>();
        mockEvents.SetupSequence(m => m.Load<MatchCanceledEvent>(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MatchCanceledEvent>() { fakeEvent })
            .ReturnsAsync(new List<MatchCanceledEvent>());

        var mockMatchRepo = new Mock<IMatchRepository>();
        mockMatchRepo.Setup(m => m.LoadDetailsByOngoingMatchId(It.IsAny<string>())).ReturnsAsync((MatchupDetail)null);

        var versionRepository = new VersionRepository(MongoClient);
        var trackingService = TestDtoHelper.CreateMockTrackingService();

        var handler = new MatchCanceledReadModelHandler<OngoingRemovalMatchCanceledHandler>(
            mockEvents.Object,
            versionRepository,
            new OngoingRemovalMatchCanceledHandler(mockMatchRepo.Object),
            trackingService.Object);

        await handler.Update();

        mockMatchRepo.Verify(m => m.DeleteOnGoingMatch(It.IsAny<Matchup>()), Times.Never);
    }


    [Test]
    public void CanceledMatchIllegalState()
    {
        var fakeEvent = TestDtoHelper.CreateFakeMatchCanceledEvent();

        fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";
        fakeEvent.match.state = EMatchState.FINISHED; // Set it to an illegal state
        var mockEvents = new Mock<IMatchEventRepository>();
        mockEvents.SetupSequence(m => m.Load<MatchCanceledEvent>(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MatchCanceledEvent>() { fakeEvent })
            .ReturnsAsync(new List<MatchCanceledEvent>());

        var mockMatchRepo = new Mock<IMatchRepository>();
        var mockTrackingService = TestDtoHelper.CreateMockTrackingService();

        var versionRepository = new VersionRepository(MongoClient);

        var handler = new MatchCanceledReadModelHandler<OngoingRemovalMatchCanceledHandler>(
            mockEvents.Object,
            versionRepository,
            new OngoingRemovalMatchCanceledHandler(mockMatchRepo.Object),
            mockTrackingService.Object);

        Assert.ThrowsAsync<InvalidOperationException>(() => handler.Update());
        mockMatchRepo.Verify(m => m.DeleteOnGoingMatch(It.IsAny<Matchup>()), Times.Never);
        mockTrackingService.Verify(m => m.TrackException(It.IsAny<InvalidOperationException>(), It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task TestThatNewVersionIsUpdated()
    {
        var fakeEvent1 = TestDtoHelper.CreateFakeMatchCanceledEvent();
        var fakeEvent2 = TestDtoHelper.CreateFakeMatchCanceledEvent();
        var fakeEvent3 = TestDtoHelper.CreateFakeMatchCanceledEvent();
        var fakeEvent4 = TestDtoHelper.CreateFakeMatchCanceledEvent();
        var fakeEvent5 = TestDtoHelper.CreateFakeMatchCanceledEvent();

        fakeEvent1.match.season = 0;
        fakeEvent1.match.startTime = 5000;
        fakeEvent1.match.endTime = 5500;
        fakeEvent1.Id = ObjectId.GenerateNewId();
        fakeEvent2.match.season = 0;
        fakeEvent2.match.startTime = 4000;
        fakeEvent2.match.endTime = 4500;
        fakeEvent2.Id = ObjectId.GenerateNewId();
        fakeEvent3.match.season = 1;
        fakeEvent3.match.startTime = 3000;
        fakeEvent3.match.endTime = 3500;
        fakeEvent3.Id = ObjectId.GenerateNewId();
        fakeEvent4.match.season = 1;
        fakeEvent4.match.startTime = 2000;
        fakeEvent4.match.endTime = 2500;
        fakeEvent4.match.id = "Test";
        fakeEvent4.Id = ObjectId.GenerateNewId();
        fakeEvent5.match.season = 0;
        fakeEvent5.match.startTime = 1000;
        fakeEvent5.match.endTime = 1500;
        fakeEvent5.Id = ObjectId.GenerateNewId();

        await InsertMatchCanceledEvents(new List<MatchCanceledEvent> { fakeEvent1, fakeEvent2, fakeEvent3, fakeEvent4, fakeEvent5 });

        var matchRepository = new MatchRepository(MongoClient, new OngoingMatchesCache(MongoClient));
        var versionRepository = new VersionRepository(MongoClient);
        var trackingService = TestDtoHelper.CreateMockTrackingService();

        var handler = new MatchCanceledReadModelHandler<OngoingRemovalMatchCanceledHandler>(
            new MatchEventRepository(MongoClient),
            versionRepository,
            new OngoingRemovalMatchCanceledHandler(matchRepository),
            trackingService.Object);

        await handler.Update();

        var version = await versionRepository.GetLastVersion<OngoingRemovalMatchCanceledHandler>();

        var matches = await matchRepository.Load(1, GameMode.GM_1v1);

        Assert.AreEqual(1, version.Season);
        Assert.AreEqual(fakeEvent5.Id.ToString(), version.Version);
        Assert.AreEqual(0, matches.Count);
    }
}
