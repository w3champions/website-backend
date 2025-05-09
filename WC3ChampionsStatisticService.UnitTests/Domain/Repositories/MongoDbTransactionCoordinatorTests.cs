using Moq;
using MongoDB.Driver;
using NUnit.Framework;
using System;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using System.Threading;
using MongoDB.Driver.Core.Bindings;

namespace WC3ChampionsStatisticService.Tests.Domain.Repositories;

[TestFixture]
public class MongoDbTransactionCoordinatorTests
{
    private Mock<IClientSessionHandle> _mockSession;
    private Mock<IMongoClient> _mockMongoClient;
    // In a real scenario with IntegrationTestBase, you might get MongoClient from the base class.
    // For pure unit tests of MongoDbTransactionCoordinator, mocking MongoClient is appropriate.

    [SetUp]
    public void MongoDbCoordinatorSetUp()
    {
        _mockSession = new Mock<IClientSessionHandle>();
        _mockMongoClient = new Mock<IMongoClient>();

        _mockMongoClient.Setup(c => c.StartSessionAsync(It.IsAny<ClientSessionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockSession.Object);

        // Default transaction state for session mock, can be overridden
        _mockSession.Setup(s => s.IsInTransaction).Returns(false);
    }

    private MongoDbTransactionCoordinator CreateCoordinator()
    {
        return new MongoDbTransactionCoordinator(_mockMongoClient.Object);
    }

    [Test]
    public async Task BeginTransactionAsync_StartsSessionAndTransaction_WhenNoneActive()
    {
        var coordinator = CreateCoordinator();
        // IsInTransaction returns false initially by default or specific setup

        await coordinator.BeginTransactionAsync();

        _mockMongoClient.Verify(c => c.StartSessionAsync(null, default), Times.Once);
        _mockSession.Verify(s => s.StartTransaction(null), Times.Once);
        // To accurately test IsTransactionActive, we need to make the mock reflect the change
        _mockSession.Setup(s => s.IsInTransaction).Returns(true);
        Assert.IsTrue(coordinator.IsTransactionActive);
    }

    [Test]
    public void BeginTransactionAsync_Throws_WhenTransactionAlreadyActive()
    {
        var coordinator = CreateCoordinator();
        Assert.DoesNotThrowAsync(async () => await coordinator.BeginTransactionAsync());
        _mockSession.Setup(s => s.IsInTransaction).Returns(true);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await coordinator.BeginTransactionAsync());
    }

    [Test]
    public async Task CommitTransactionAsync_CommitsActiveTransaction_AndRunsSuccessHandlers()
    {
        var coordinator = CreateCoordinator();
        bool handler1Run = false;
        bool handler2Run = false;

        // Setup for BeginTransactionAsync
        _mockSession.Setup(s => s.IsInTransaction).Returns(false);
        await coordinator.BeginTransactionAsync();

        // After BeginTransactionAsync, session is now in transaction
        _mockSession.Setup(s => s.IsInTransaction).Returns(true);

        await coordinator.RegisterOnSuccessHandler(async () => { handler1Run = true; await Task.CompletedTask; });
        await coordinator.RegisterOnSuccessHandler(async () => { handler2Run = true; await Task.CompletedTask; });

        await coordinator.CommitTransactionAsync();

        _mockSession.Verify(s => s.CommitTransactionAsync(default), Times.Once);
        Assert.IsTrue(handler1Run, "Handler 1 should run on commit.");
        Assert.IsTrue(handler2Run, "Handler 2 should run on commit.");
    }

    [Test]
    public void CommitTransactionAsync_Throws_WhenNoTransactionActive()
    {
        var coordinator = CreateCoordinator();
        _mockSession.Setup(s => s.IsInTransaction).Returns(false);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await coordinator.CommitTransactionAsync());
    }

    [Test]
    public async Task AbortTransactionAsync_AbortsActiveTransaction_AndClearsSuccessHandlers()
    {
        var coordinator = CreateCoordinator();
        bool handlerRun = false;

        _mockSession.Setup(s => s.IsInTransaction).Returns(false); // For Begin
        await coordinator.BeginTransactionAsync();
        _mockSession.Setup(s => s.IsInTransaction).Returns(true); // Now active

        await coordinator.RegisterOnSuccessHandler(async () => { handlerRun = true; await Task.CompletedTask; });
        await coordinator.AbortTransactionAsync();

        _mockSession.Verify(s => s.AbortTransactionAsync(default), Times.Once);
        Assert.IsFalse(handlerRun, "Handler should not run on abort because Commit was not called.");
    }

    [Test]
    public async Task AbortTransactionAsync_DoesNotCallAbortOnSession_WhenNoTransactionActive()
    {
        var coordinator = CreateCoordinator();
        _mockSession.Setup(s => s.IsInTransaction).Returns(false);

        await coordinator.AbortTransactionAsync();
        _mockSession.Verify(s => s.AbortTransactionAsync(default), Times.Never);
    }

    [Test]
    public async Task RegisterOnSuccessHandler_WhenActive_AddsHandler()
    {
        var coordinator = CreateCoordinator();
        _mockSession.Setup(s => s.IsInTransaction).Returns(false); // For Begin
        await coordinator.BeginTransactionAsync();
        _mockSession.Setup(s => s.IsInTransaction).Returns(true); // Now active

        Func<Task> handler = () => Task.CompletedTask;
        await coordinator.RegisterOnSuccessHandler(handler);

        // Verify handler runs on commit
        bool testHandlerRun = false;
        await coordinator.RegisterOnSuccessHandler(async () => { testHandlerRun = true; await Task.CompletedTask; });
        await coordinator.CommitTransactionAsync();
        Assert.IsTrue(testHandlerRun, "Registered handler should run on commit.");
    }

    [Test]
    public async Task RegisterOnSuccessHandler_WhenNotActive_ExecutesImmediately_IfAllowed()
    {
        var coordinator = CreateCoordinator();
        _mockSession.Setup(s => s.IsInTransaction).Returns(false);
        bool handlerExecuted = false;
        Func<Task> handler = () => { handlerExecuted = true; return Task.CompletedTask; };

        await coordinator.RegisterOnSuccessHandler(handler, true);
        Assert.IsTrue(handlerExecuted);
    }

    [Test]
    public void RegisterOnSuccessHandler_WhenNotActive_Throws_IfNotAllowedToExecuteImmediately()
    {
        var coordinator = CreateCoordinator();
        _mockSession.Setup(s => s.IsInTransaction).Returns(false);
        Func<Task> handler = () => Task.CompletedTask;

        Assert.ThrowsAsync<InvalidOperationException>(async () => await coordinator.RegisterOnSuccessHandler(handler, false));
    }

    [Test]
    public async Task Dispose_DisposesSession_IfSessionWasStarted()
    {
        var coordinator = CreateCoordinator();
        // Simulate having an active session by calling BeginTransaction
        _mockSession.Setup(s => s.IsInTransaction).Returns(false);
        await coordinator.BeginTransactionAsync();

        coordinator.Dispose();
        _mockSession.Verify(s => s.Dispose(), Times.Once);
    }

    [Test]
    public void Dispose_DoesNotAttemptToDisposeNullSession()
    {
        var coordinator = CreateCoordinator();
        // Never call BeginTransactionAsync, so _session remains null
        Assert.DoesNotThrow(() => coordinator.Dispose());
        _mockSession.Verify(s => s.Dispose(), Times.Never); // Session was never started, so not disposed by coordinator
    }
}
