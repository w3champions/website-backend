using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using System;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Matches;
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

    [Test]
    public async Task RollbackWorksForMatches()
    {
        var matchRepository = new MatchRepository(
            MongoClient,
            new OngoingMatchesCache(MongoClient, _transactionCoordinator),
            _transactionCoordinator);
        var matches = await matchRepository.LoadOnGoingMatches();
        Assert.AreEqual(0, matches.Count);
        var matchup = OnGoingMatchup.Create(TestDtoHelper.CreateFakeStartedEvent());

        await using (var transactionScope = AsyncTransactionScope.Create(_transactionCoordinator))
        {
            await transactionScope.Start();
            await matchRepository.InsertOnGoingMatch(matchup);
            var dbMatch = await matchRepository.LoadOnGoingMatchForPlayer(matchup.Teams[0].Players[0].BattleTag);
            Assert.IsNotNull(dbMatch);
            transactionScope.Complete(); // Commit
        }

        await using (var transactionScope = AsyncTransactionScope.Create(_transactionCoordinator))
        {
            await transactionScope.Start();
            var dbMatch2 = await matchRepository.LoadOnGoingMatchForPlayer(matchup.Teams[0].Players[0].BattleTag);
            Assert.IsNotNull(dbMatch2);
            await matchRepository.DeleteOnGoingMatch(dbMatch2);
            dbMatch2 = await matchRepository.LoadOnGoingMatchForPlayer(matchup.Teams[0].Players[0].BattleTag);
            Assert.IsNull(dbMatch2);
            // No Complete, roll back
            transactionScope.Complete();
        }

        await using (var transactionScope = AsyncTransactionScope.Create(_transactionCoordinator))
        {
            await transactionScope.Start();
            var dbMatch3 = await matchRepository.LoadOnGoingMatchForPlayer(matchup.Teams[0].Players[0].BattleTag);
            Assert.IsNotNull(dbMatch3);
            await matchRepository.DeleteOnGoingMatch(dbMatch3);
            dbMatch3 = await matchRepository.LoadOnGoingMatchForPlayer(matchup.Teams[0].Players[0].BattleTag);
            Assert.IsNull(dbMatch3);
            transactionScope.Complete();
        }

        var dbMatch4 = await matchRepository.LoadOnGoingMatchForPlayer(matchup.Teams[0].Players[0].BattleTag);
        Assert.IsNull(dbMatch4);
        var matches2 = await matchRepository.LoadOnGoingMatches();
        Assert.AreEqual(0, matches2.Count);
    }
}
