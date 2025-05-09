using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using System;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
namespace WC3ChampionsStatisticService.Tests.Domain.Repositories;

[TestFixture]
public class AsyncTransactionScopeTests : IntegrationTestBase
{
    private MongoDbTransactionCoordinator _transactionCoordinator;

    [SetUp]
    public void SetupTest()
    {
        _transactionCoordinator = new MongoDbTransactionCoordinator(MongoClient);
    }

    [Test]
    public void CreateAsync_NullCoordinator_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => AsyncTransactionScope.Create(null));
    }

    [Test]
    public async Task DisposeAsync_WhenCompleted_CallsCommitTransaction()
    {
        bool handlerRun = false;
        await using (var scope = AsyncTransactionScope.Create(_transactionCoordinator))
        {
            await scope.Start();
            await scope.RegisterOnSuccessHandler(async () =>
            {
                handlerRun = true;
            });
            scope.Complete();
        }

        Assert.IsTrue(handlerRun);
    }

    [Test]
    public async Task DisposeAsync_WhenNotCompleted_CallsAbortTransaction()
    {
        bool handlerRun = false;
        await using (var scope = AsyncTransactionScope.Create(_transactionCoordinator))
        {
            await scope.Start();
            await scope.RegisterOnSuccessHandler(async () =>
            {
                handlerRun = true;
            });
            // No call to scope.Complete()
        }

        Assert.IsFalse(handlerRun);
    }


    [Test]
    public async Task Complete_WhenTransactionNotActive_ThrowsInvalidOperationException()
    {
        await using (var scope = AsyncTransactionScope.Create(_transactionCoordinator))
        {
            // No call to scope.Start();
            Assert.Throws<InvalidOperationException>(() => scope.Complete());
        }
    }
}
