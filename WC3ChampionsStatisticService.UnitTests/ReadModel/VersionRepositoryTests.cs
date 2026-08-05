using System.Threading.Tasks;
using MongoDB.Bson;
using NUnit.Framework;
using W3ChampionsStatisticService.ReadModelBase;

namespace WC3ChampionsStatisticService.Tests.ReadModel;

/// <summary>
/// The poison event guard in <see cref="MatchEventReadModelHandler{TEvent, THandler}"/> only works if
/// the retry budget survives between handler instances - the handlers are registered transient, so a
/// fresh one is built for every 5 second retry cycle. That makes these persistence semantics the load
/// bearing part of the guard rather than an implementation detail.
/// </summary>
[TestFixture]
public class VersionRepositoryTests : IntegrationTestBase
{
    // Only used as a type key for the HandlerVersions document; never instantiated.
    private sealed class FakeHandler;

    [Test]
    public async Task RecordEventFailure_CountsRepeatedFailuresOfTheSameEvent()
    {
        var versionRepository = new VersionRepository(MongoClient);
        var eventId = ObjectId.GenerateNewId().ToString();

        Assert.AreEqual(1, await versionRepository.RecordEventFailure<FakeHandler>(eventId));
        Assert.AreEqual(2, await versionRepository.RecordEventFailure<FakeHandler>(eventId));
        Assert.AreEqual(3, await versionRepository.RecordEventFailure<FakeHandler>(eventId));
    }

    [Test]
    public async Task RecordEventFailure_RestartsTheCountForADifferentEvent()
    {
        var versionRepository = new VersionRepository(MongoClient);
        var firstEventId = ObjectId.GenerateNewId().ToString();
        var secondEventId = ObjectId.GenerateNewId().ToString();

        await versionRepository.RecordEventFailure<FakeHandler>(firstEventId);
        await versionRepository.RecordEventFailure<FakeHandler>(firstEventId);
        await versionRepository.RecordEventFailure<FakeHandler>(firstEventId);

        // A new event must never inherit the previous event's budget, otherwise it would be skipped
        // almost immediately instead of getting its own transient-error tolerance.
        Assert.AreEqual(1, await versionRepository.RecordEventFailure<FakeHandler>(secondEventId));
    }

    [Test]
    public async Task RecordEventFailure_DoesNotLoseTheWatermarkOfAnUntrackedHandler()
    {
        var versionRepository = new VersionRepository(MongoClient);
        var eventId = ObjectId.GenerateNewId().ToString();

        // The very first event a handler ever sees can fail before any watermark was written.
        await versionRepository.RecordEventFailure<FakeHandler>(eventId);

        var version = await versionRepository.GetLastVersion<FakeHandler>();
        Assert.AreEqual(ObjectId.Empty.ToString(), version.Version);
        Assert.IsFalse(version.IsStopped);
    }

    [Test]
    public async Task SaveLastVersion_ClearsTheFailureBudget()
    {
        var versionRepository = new VersionRepository(MongoClient);
        var eventId = ObjectId.GenerateNewId().ToString();

        await versionRepository.RecordEventFailure<FakeHandler>(eventId);
        await versionRepository.RecordEventFailure<FakeHandler>(eventId);

        await versionRepository.SaveLastVersion<FakeHandler>(eventId, 1);

        // Forward progress resets the budget, so the same event failing again later starts over.
        Assert.AreEqual(1, await versionRepository.RecordEventFailure<FakeHandler>(eventId));
    }

    [Test]
    public async Task SaveLastVersion_PreservesTheWatermarkAndSeason()
    {
        var versionRepository = new VersionRepository(MongoClient);
        var eventId = ObjectId.GenerateNewId().ToString();

        await versionRepository.RecordEventFailure<FakeHandler>(eventId);
        await versionRepository.SaveLastVersion<FakeHandler>(eventId, 3);

        var version = await versionRepository.GetLastVersion<FakeHandler>();
        Assert.AreEqual(eventId, version.Version);
        Assert.AreEqual(3, version.Season);
    }
}
