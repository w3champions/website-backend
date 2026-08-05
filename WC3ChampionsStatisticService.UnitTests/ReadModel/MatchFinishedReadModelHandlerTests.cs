using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
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

    [TestCase(EMatchState.CANCELED)]
    [TestCase(EMatchState.INIT)]
    [TestCase(EMatchState.STARTED)]
    public async Task IllegalMatchState_IsSkippedAndWatermarkAdvances(EMatchState illegalState)
    {
        // A match's state never changes after the fact, so an event that does not belong to this
        // handler is a permanent condition. Retrying it forever wedges the whole handler, so it is
        // logged and skipped instead - and the watermark still has to move past it.
        var fakeEvent = TestDtoHelper.CreateFakeEvent();

        fakeEvent.match.map = "Maps/frozenthrone/community/(2)amazonia.w3x";
        fakeEvent.match.state = illegalState;
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

        Assert.DoesNotThrowAsync(() => handler.Update());

        mockMatchRepo.Verify(m => m.Insert(It.IsAny<Matchup>()), Times.Never);
        mockTrackingService.Verify(m => m.TrackException(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);

        var version = await versionRepository.GetLastVersion<MatchReadModelHandler>();
        Assert.AreEqual(fakeEvent.Id.ToString(), version.Version);
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

    // ---------------------------------------------------------------------------------------------
    // Poison event guard. AsyncServiceBase creates a fresh handler instance and re-enters Update()
    // every 5 seconds, so the retry budget only works if it lives in the HandlerVersions store.
    // ---------------------------------------------------------------------------------------------

    // Mirrors the production budget; asserted to stay in sync in MaxAttemptsMatchesTheProductionBudget.
    private const int MaxAttempts = 60;

    private const string FinishedHandlerName = nameof(IMatchFinishedReadModelHandler);

    /// <summary>Behaves like the real MatchEventRepository.Load: everything after the watermark, in id order.</summary>
    private static Mock<IMatchEventRepository> CreatePagingEventRepository(List<MatchFinishedEvent> events)
    {
        var mockEvents = new Mock<IMatchEventRepository>();
        mockEvents
            .Setup(m => m.Load<MatchFinishedEvent>(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync((string lastObjectId, int pageSize) => events
                .Where(e => e.Id > ObjectId.Parse(lastObjectId ?? ObjectId.Empty.ToString()))
                .OrderBy(e => e.Id)
                .Take(pageSize)
                .ToList());
        return mockEvents;
    }

    private static MatchFinishedEvent CreateFinishedEvent()
    {
        var fakeEvent = TestDtoHelper.CreateFakeEvent();
        fakeEvent.match.state = EMatchState.FINISHED;
        return fakeEvent;
    }

    /// <summary>Reads the raw handler version document, so the persisted field names are pinned too.</summary>
    private async Task<BsonDocument> LoadHandlerVersionDocument()
    {
        var collection = MongoClient
            .GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<BsonDocument>("HandlerVersions");
        return await collection
            .Find(Builders<BsonDocument>.Filter.Eq("HandlerName", FinishedHandlerName))
            .FirstOrDefaultAsync();
    }

    // A cleared budget may be stored as an absent field or an explicit zero/null - both mean "no
    // failure recorded", so the assertions below read through that rather than pinning the shape.
    private async Task<int> LoadFailureCount()
    {
        var document = await LoadHandlerVersionDocument();
        return document.GetValue("FailureCount", 0).ToInt32();
    }

    private async Task<string> LoadFailingEventId()
    {
        var document = await LoadHandlerVersionDocument();
        var failingEventId = document.GetValue("FailingEventId", BsonNull.Value);
        return failingEventId.IsBsonNull ? null : failingEventId.AsString;
    }

    [Test]
    public void MaxAttemptsMatchesTheProductionBudget()
    {
        Assert.AreEqual(
            MatchEventReadModelHandler<MatchFinishedEvent, IMatchFinishedReadModelHandler>.MaxEventFailuresBeforeSkip,
            MaxAttempts);
    }

    [Test]
    public async Task TransientFailure_IsRetriedAndDoesNotAdvanceTheWatermark()
    {
        var fakeEvent = CreateFinishedEvent();
        var mockEvents = CreatePagingEventRepository([fakeEvent]);
        var mockTrackingService = TestDtoHelper.CreateMockTrackingService();
        var versionRepository = new VersionRepository(MongoClient);

        var shouldFail = true;
        var innerHandlerMock = new Mock<IMatchFinishedReadModelHandler>();
        innerHandlerMock
            .Setup(h => h.Update(It.IsAny<MatchFinishedEvent>()))
            .Returns(() => shouldFail
                ? Task.FromException(new IOException("transient mongo blip"))
                : Task.CompletedTask);

        var handler = new MatchFinishedReadModelHandler<IMatchFinishedReadModelHandler>(
            mockEvents.Object,
            versionRepository,
            innerHandlerMock.Object,
            mockTrackingService.Object);

        // Below the budget the exception must still bubble up, so AsyncServiceBase retries in 5s.
        Assert.ThrowsAsync<IOException>(() => handler.Update());

        var afterFailure = await versionRepository.GetLastVersion<IMatchFinishedReadModelHandler>();
        Assert.AreEqual(ObjectId.Empty.ToString(), afterFailure.Version,
            "A retryable failure must not advance the watermark - the event would be lost");

        shouldFail = false;
        await handler.Update();

        var afterSuccess = await versionRepository.GetLastVersion<IMatchFinishedReadModelHandler>();
        Assert.AreEqual(fakeEvent.Id.ToString(), afterSuccess.Version,
            "Once the transient error clears, the very same event must be processed and committed");
    }

    [Test]
    public async Task SuccessResetsTheFailureCounter()
    {
        var fakeEvent = CreateFinishedEvent();
        var mockEvents = CreatePagingEventRepository([fakeEvent]);
        var mockTrackingService = TestDtoHelper.CreateMockTrackingService();
        var versionRepository = new VersionRepository(MongoClient);

        var shouldFail = true;
        var innerHandlerMock = new Mock<IMatchFinishedReadModelHandler>();
        innerHandlerMock
            .Setup(h => h.Update(It.IsAny<MatchFinishedEvent>()))
            .Returns(() => shouldFail
                ? Task.FromException(new IOException("transient mongo blip"))
                : Task.CompletedTask);

        var handler = new MatchFinishedReadModelHandler<IMatchFinishedReadModelHandler>(
            mockEvents.Object,
            versionRepository,
            innerHandlerMock.Object,
            mockTrackingService.Object);

        Assert.ThrowsAsync<IOException>(() => handler.Update());
        Assert.ThrowsAsync<IOException>(() => handler.Update());

        Assert.AreEqual(2, await LoadFailureCount(),
            "Repeated failures on the same event must accumulate across handler instances");
        Assert.AreEqual(fakeEvent.Id.ToString(), await LoadFailingEventId());

        shouldFail = false;
        await handler.Update();

        Assert.AreEqual(0, await LoadFailureCount(), "Forward progress must reset the retry budget");
        Assert.That(await LoadFailingEventId(), Is.Null);
    }

    [Test]
    public async Task PersistentlyFailingEvent_IsSkippedAfterMaxAttempts_AndProcessingContinues()
    {
        var poisonEvent = CreateFinishedEvent();
        var healthyEvent = CreateFinishedEvent();
        // ObjectIds are monotonic, so the poison event is the first one the handler sees.
        Assert.That(poisonEvent.Id, Is.LessThan(healthyEvent.Id));

        var mockEvents = CreatePagingEventRepository([poisonEvent, healthyEvent]);
        var mockTrackingService = TestDtoHelper.CreateMockTrackingService();
        var versionRepository = new VersionRepository(MongoClient);

        var processed = new List<ObjectId>();
        var innerHandlerMock = new Mock<IMatchFinishedReadModelHandler>();
        innerHandlerMock
            .Setup(h => h.Update(It.IsAny<MatchFinishedEvent>()))
            .Returns((MatchFinishedEvent e) =>
            {
                if (e.Id == poisonEvent.Id)
                {
                    return Task.FromException(new IOException("this event can never be processed"));
                }
                processed.Add(e.Id);
                return Task.CompletedTask;
            });

        var handler = new MatchFinishedReadModelHandler<IMatchFinishedReadModelHandler>(
            mockEvents.Object,
            versionRepository,
            innerHandlerMock.Object,
            mockTrackingService.Object);

        // Every cycle up to the budget keeps retrying and keeps the stream blocked.
        for (var attempt = 1; attempt < MaxAttempts; attempt++)
        {
            Assert.ThrowsAsync<IOException>(() => handler.Update());
        }
        Assert.That(processed, Is.Empty, "Nothing may be processed while the poison event still blocks the stream");

        // The cycle that hits the budget gives up on the poison event and drains the rest.
        Assert.DoesNotThrowAsync(() => handler.Update());

        Assert.That(processed, Is.EqualTo(new List<ObjectId> { healthyEvent.Id }),
            "Subsequent events must be processed once the poison event is skipped");

        var version = await versionRepository.GetLastVersion<IMatchFinishedReadModelHandler>();
        Assert.AreEqual(healthyEvent.Id.ToString(), version.Version);

        // One tracked exception per attempt, plus the distinct one for the skip itself.
        mockTrackingService.Verify(
            m => m.TrackException(It.IsAny<Exception>(), It.IsAny<string>()),
            Times.Exactly(MaxAttempts + 1));
    }
}
