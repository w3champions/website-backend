using Moq;
using NUnit.Framework;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.Services;
using W3C.Contracts.Matchmaking;
using MongoDB.Bson;
using System;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Matches;

namespace WC3ChampionsStatisticService.Tests.Matches;

// Helper class to implement the structure required by CanceledMatchesHandler
// We create this because the handler calls ongoingMatch.Match.Id.ToString() 
// and whatever object 'Match' refers to must have an ObjectId property named 'Id'
public class MatchWithObjectId
{
    public ObjectId Id { get; set; }
    // Add other properties if CanceledMatchesHandler accesses them via ongoingMatch.Match
}

[TestFixture]
public class CanceledMatchesHandlerTests
{
    private Mock<IMatchEventRepository> _mockEventRepo;
    private Mock<IMatchRepository> _mockMatchRepo;
    private Mock<ITransactionCoordinator> _mockTransactionCoordinator;
    private Mock<ITrackingService> _mockTrackingService;
    private CanceledMatchesHandler _handler;

    [SetUp]
    public void Setup()
    {
        _mockEventRepo = new Mock<IMatchEventRepository>();
        _mockMatchRepo = new Mock<IMatchRepository>();
        _mockTransactionCoordinator = new Mock<ITransactionCoordinator>();
        _mockTrackingService = new Mock<ITrackingService>();

        _handler = new CanceledMatchesHandler(
            _mockEventRepo.Object,
            _mockMatchRepo.Object,
            _mockTransactionCoordinator.Object,
            _mockTrackingService.Object);

        _mockTransactionCoordinator.Setup(t => t.BeginTransactionAsync(default)).Returns(Task.CompletedTask);
        _mockTransactionCoordinator.Setup(t => t.CommitTransactionAsync(default)).Returns(Task.CompletedTask);
        _mockTransactionCoordinator.Setup(t => t.AbortTransactionAsync(default)).Returns(Task.CompletedTask);
        _mockTransactionCoordinator.Setup(t => t.IsTransactionActive).Returns(true);
    }

    private MatchCanceledEvent CreateFakeMatchCanceledEvent(string matchId, GameMode gameMode = GameMode.GM_1v1)
    {
        return new MatchCanceledEvent
        {
            Id = ObjectId.GenerateNewId(),
            match = new W3C.Domain.MatchmakingService.Match { id = matchId, gameMode = gameMode }
        };
    }

    [Test]
    public async Task Update_NoCanceledEvents_DoesNothing()
    {
        _mockEventRepo.Setup(r => r.LoadCanceledMatches())
            .Returns(Task.FromResult(new List<MatchCanceledEvent>()));

        await _handler.Update();

        _mockMatchRepo.Verify(r => r.DeleteOnGoingMatch(It.IsAny<Matchup>()), Times.Never);
        _mockEventRepo.Verify(r => r.DeleteCanceledEvent(It.IsAny<ObjectId>()), Times.Never);
        _mockTransactionCoordinator.Verify(t => t.BeginTransactionAsync(default), Times.Never);
    }

    [Test]
    public async Task Update_ProcessesNonCustomGame_DeletesOngoingMatchAndEvent()
    {
        var eventMatchId = "someFakeMatchStringId123";
        var canceledEvent = CreateFakeMatchCanceledEvent(eventMatchId, GameMode.GM_1v1);
        var dbObjectId = ObjectId.GenerateNewId();

        // Create a mock MatchupDetail with an Id that can be converted to string
        var mockMatchupDetail = new Mock<MatchupDetail>();

        // Setup the Mock to capture the Matchup object that will be passed to DeleteOnGoingMatch
        Matchup capturedMatchup = null;
        _mockMatchRepo.Setup(r => r.DeleteOnGoingMatch(It.IsAny<Matchup>()))
            .Callback<Matchup>(m => capturedMatchup = m)
            .Returns(Task.CompletedTask);

        _mockEventRepo.SetupSequence(r => r.LoadCanceledMatches())
            .Returns(Task.FromResult(new List<MatchCanceledEvent> { canceledEvent }))
            .Returns(Task.FromResult(new List<MatchCanceledEvent>()));

        // Mock this call to return a MatchupDetail with a known MatchId
        // The CanceledMatchesHandler implementation must now construct a Matchup from this
        _mockMatchRepo.Setup(r => r.LoadDetailsByOngoingMatchId(eventMatchId))
            .Returns(Task.FromResult(mockMatchupDetail.Object));

        await _handler.Update();

        _mockTransactionCoordinator.Verify(t => t.BeginTransactionAsync(default), Times.Once);
        _mockMatchRepo.Verify(r => r.DeleteOnGoingMatch(It.IsAny<Matchup>()), Times.Once);
        _mockEventRepo.Verify(r => r.DeleteCanceledEvent(canceledEvent.Id), Times.Once);
        _mockTransactionCoordinator.Verify(t => t.CommitTransactionAsync(default), Times.Once);
        _mockTransactionCoordinator.Verify(t => t.AbortTransactionAsync(default), Times.Never);
    }

    [Test]
    public async Task Update_ProcessesCustomGame_DeletesOnlyEvent()
    {
        var matchId = "customGameMatchId789";
        var canceledEvent = CreateFakeMatchCanceledEvent(matchId, GameMode.CUSTOM);

        _mockEventRepo.SetupSequence(r => r.LoadCanceledMatches())
            .Returns(Task.FromResult(new List<MatchCanceledEvent> { canceledEvent }))
            .Returns(Task.FromResult(new List<MatchCanceledEvent>()));

        await _handler.Update();

        _mockTransactionCoordinator.Verify(t => t.BeginTransactionAsync(default), Times.Once);
        _mockMatchRepo.Verify(r => r.DeleteOnGoingMatch(It.IsAny<Matchup>()), Times.Never);
        _mockMatchRepo.Verify(r => r.LoadDetailsByOngoingMatchId(It.IsAny<string>()), Times.Never);
        _mockEventRepo.Verify(r => r.DeleteCanceledEvent(canceledEvent.Id), Times.Once);
        _mockTransactionCoordinator.Verify(t => t.CommitTransactionAsync(default), Times.Once);
    }

    [Test]
    public async Task Update_OngoingMatchNotFound_LogsWarningAndContinues_DeletesEvent()
    {
        var matchId = "nonExistentMatchId456";
        var canceledEvent = CreateFakeMatchCanceledEvent(matchId, GameMode.GM_1v1);

        _mockEventRepo.SetupSequence(r => r.LoadCanceledMatches())
            .Returns(Task.FromResult(new List<MatchCanceledEvent> { canceledEvent }))
            .Returns(Task.FromResult(new List<MatchCanceledEvent>()));

        _mockMatchRepo.Setup(r => r.LoadDetailsByOngoingMatchId(matchId))
            .Returns(Task.FromResult<MatchupDetail>(null));

        await _handler.Update();

        _mockTransactionCoordinator.Verify(t => t.BeginTransactionAsync(default), Times.Once);
        _mockMatchRepo.Verify(r => r.DeleteOnGoingMatch(It.IsAny<Matchup>()), Times.Never);
        _mockEventRepo.Verify(r => r.DeleteCanceledEvent(canceledEvent.Id), Times.Once);
        _mockTransactionCoordinator.Verify(t => t.CommitTransactionAsync(default), Times.Once);
    }

    [Test]
    public void Update_ExceptionDuringProcessing_AbortsTransactionAndTracksException()
    {
        var matchId = "errorMatchId101";
        var canceledEvent = CreateFakeMatchCanceledEvent(matchId, GameMode.GM_1v1);
        var expectedException = new Exception("Database error");

        _mockEventRepo.SetupSequence(r => r.LoadCanceledMatches())
            .Returns(Task.FromResult(new List<MatchCanceledEvent> { canceledEvent }))
            .Returns(Task.FromResult(new List<MatchCanceledEvent>()));

        _mockMatchRepo.Setup(r => r.LoadDetailsByOngoingMatchId(matchId))
            .ThrowsAsync(expectedException);

        // Use Throws.TypeOf to assert that an exception is thrown
        Assert.That(async () => await _handler.Update(), Throws.TypeOf<Exception>());

        _mockTransactionCoordinator.Verify(t => t.BeginTransactionAsync(default), Times.Once);
        _mockTransactionCoordinator.Verify(t => t.CommitTransactionAsync(default), Times.Never);
        _mockTransactionCoordinator.Verify(t => t.AbortTransactionAsync(default), Times.Once);
        _mockTrackingService.Verify(ts => ts.TrackException(expectedException, $"CanceledMatchesHandler died on event {canceledEvent.Id}"), Times.Once);
        _mockEventRepo.Verify(r => r.DeleteCanceledEvent(It.IsAny<ObjectId>()), Times.Never);
    }

    [Test]
    public void Update_ExceptionDeletingEvent_AbortsTransactionAndTracksException()
    {
        var matchId = "eventDeleteErrorMatchId102";
        var canceledEvent = CreateFakeMatchCanceledEvent(matchId, GameMode.CUSTOM);
        var expectedException = new Exception("Event delete error");

        _mockEventRepo.SetupSequence(r => r.LoadCanceledMatches())
            .Returns(Task.FromResult(new List<MatchCanceledEvent> { canceledEvent }))
            .Returns(Task.FromResult(new List<MatchCanceledEvent>()));

        _mockEventRepo.Setup(r => r.DeleteCanceledEvent(canceledEvent.Id))
            .ThrowsAsync(expectedException);

        // Use Throws.TypeOf to assert that an exception is thrown
        Assert.That(async () => await _handler.Update(), Throws.TypeOf<Exception>());

        _mockTransactionCoordinator.Verify(t => t.BeginTransactionAsync(default), Times.Once);
        _mockTransactionCoordinator.Verify(t => t.CommitTransactionAsync(default), Times.Never);
        _mockTransactionCoordinator.Verify(t => t.AbortTransactionAsync(default), Times.Once);
        _mockTrackingService.Verify(ts => ts.TrackException(expectedException, $"CanceledMatchesHandler died on event {canceledEvent.Id}"), Times.Once);
    }
}
