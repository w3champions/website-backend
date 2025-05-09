using Moq;
using NUnit.Framework;
using System;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
namespace WC3ChampionsStatisticService.Tests.Domain.Repositories;

[TestFixture]
public class AsyncTransactionScopeTests
{
    private Mock<ITransactionCoordinator> _mockCoordinator;

    [SetUp]
    public void SetUp()
    {
        _mockCoordinator = new Mock<ITransactionCoordinator>();
        // Default setup for IsTransactionActive, can be overridden in specific tests
        _mockCoordinator.Setup(c => c.IsTransactionActive).Returns(true);
    }

    [Test]
    public async Task CreateAsync_CallsBeginTransaction()
    {
        await using (var scope = await AsyncTransactionScope.CreateAsync(_mockCoordinator.Object))
        {
            _mockCoordinator.Verify(c => c.BeginTransactionAsync(default), Times.Once);
        }
    }

    [Test]
    public void CreateAsync_NullCoordinator_ThrowsArgumentNullException()
    {
        Assert.ThrowsAsync<ArgumentNullException>(async () => await AsyncTransactionScope.CreateAsync(null));
    }

    [Test]
    public async Task DisposeAsync_WhenCompleted_CallsCommitTransaction()
    {
        await using (var scope = await AsyncTransactionScope.CreateAsync(_mockCoordinator.Object))
        {
            scope.Complete();
        }

        _mockCoordinator.Verify(c => c.CommitTransactionAsync(default), Times.Once);
        _mockCoordinator.Verify(c => c.AbortTransactionAsync(default), Times.Never);
    }

    [Test]
    public async Task DisposeAsync_WhenNotCompleted_CallsAbortTransaction()
    {
        await using (var scope = await AsyncTransactionScope.CreateAsync(_mockCoordinator.Object))
        {
            // No call to scope.Complete()
        }

        _mockCoordinator.Verify(c => c.AbortTransactionAsync(default), Times.Once);
        _mockCoordinator.Verify(c => c.CommitTransactionAsync(default), Times.Never);
    }


    [Test]
    public async Task Complete_WhenTransactionNotActive_ThrowsInvalidOperationException()
    {
        _mockCoordinator.Setup(c => c.IsTransactionActive).Returns(false); // Simulate transaction not active

        await using (var scope = await AsyncTransactionScope.CreateAsync(_mockCoordinator.Object))
        {
            Assert.Throws<InvalidOperationException>(() => scope.Complete());
        }
    }
}
