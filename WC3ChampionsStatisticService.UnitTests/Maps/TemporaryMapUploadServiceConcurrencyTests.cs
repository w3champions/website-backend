using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.Sessions;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// Uploads of the same fileKey serialise on <see cref="TemporaryMapFileKeyLock"/> from the store
/// through compensation, so neither can delete the other's bytes. Every interleaving is forced with signals completed by
/// scripted routes, the lock's contention hook and the compensation wait seam; nothing sleeps.
/// </summary>
[TestFixture]
public class TemporaryMapUploadServiceConcurrencyTests : TemporaryMapUploadServiceTestBase
{
    private const string Conflict = "{\"message\":\"File already exists\"}";

    [Test]
    public async Task TwoUploadsOfTheSameFileKey_NeverInterleave_AndTheSecondDedupesWithoutADelete()
    {
        // Two uploaders send the same bytes under the same name: the same fileKey, one quota each.
        var firstInCreate = NewSignal();
        var releaseCreate = NewSignal();
        var secondWaitingOrStoring = NewSignal();
        FileKeyLock = new TemporaryMapFileKeyLock { OnContended = _ => secondWaitingOrStoring.TrySetResult() };
        var handler = OnSequence(new ScriptedHttpHandler(), IsBySha1,
                Respond(HttpStatusCode.NotFound), Respond(HttpStatusCode.NotFound), Respond(HttpStatusCode.OK, Record(5811)))
            .On(IsCreate, _ =>
            {
                firstInCreate.TrySetResult();
                Assert.That(releaseCreate.Task.Wait(HangGuard), Is.True, "the test never released the first create");
                return ScriptedHttpHandler.Json(HttpStatusCode.Created, Record(5811));
            })
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        OnSequence(handler, IsUsUpload,
            Respond(HttpStatusCode.OK, UsUploadBody()),
            _ =>
            {
                // Reached early only if the second upload stores while the first still holds the fileKey.
                secondWaitingOrStoring.TrySetResult();
                return ScriptedHttpHandler.Json(HttpStatusCode.Conflict, Conflict);
            });

        var first = Task.Run(() => Run(handler));
        Task<TemporaryMapUploadOutcome> second = null;
        var storesWhileTheFirstHeldTheKey = -1;
        try
        {
            await firstInCreate.Task.WaitAsync(HangGuard);
            second = Task.Run(() => Run(handler, battleTag: OtherBattleTag));
            await secondWaitingOrStoring.Task.WaitAsync(HangGuard);
            storesWhileTheFirstHeldTheKey = Count(handler, IsUsUpload);
        }
        finally
        {
            releaseCreate.TrySetResult();
        }

        var created = await first.WaitAsync(HangGuard);
        var deduped = await second!.WaitAsync(HangGuard);

        Assert.That(storesWhileTheFirstHeldTheKey, Is.EqualTo(1), "the second store waited for the first upload's record write");
        Assert.That(created.Created, Is.True);
        Assert.That(deduped.Created, Is.False);
        Assert.That(deduped.Response.MapId, Is.EqualTo(5811));
        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[]
        {
            "by-sha1", "us-upload", "create", // the first upload, up to its create
            "by-sha1", // the second upload's dedupe probe, before it waits for the fileKey
            "us-upload", "by-sha1", // the second upload's store finds the file and matchmaking's record
        }));
        Assert.That(Count(handler, IsUsDelete), Is.Zero, "neither upload deletes the bytes the record points at");
        Assert.That(FileKeyLock.Count, Is.Zero, "both uploads released the fileKey");
    }

    [Test]
    public async Task AnUploadWaitingForTheFileKey_DoesNotStoreDuringTheFirstUploadsCompensationBackOff()
    {
        var secondWaitingOrStoring = NewSignal();
        FileKeyLock = new TemporaryMapFileKeyLock { OnContended = _ => secondWaitingOrStoring.TrySetResult() };
        var handler = UnknownSha1Handler();
        OnSequence(handler, IsCreate, Respond(HttpStatusCode.InternalServerError, "{}"), Respond(HttpStatusCode.Created, Record(5811)));
        OnSequence(handler, IsUsDelete, Respond(HttpStatusCode.ServiceUnavailable, ""), Respond(HttpStatusCode.NoContent, ""));
        OnSequence(handler, IsUsUpload,
            Respond(HttpStatusCode.OK, UsUploadBody()),
            _ =>
            {
                secondWaitingOrStoring.TrySetResult();
                return ScriptedHttpHandler.Json(HttpStatusCode.OK, UsUploadBody());
            });
        Task<TemporaryMapUploadOutcome> second = null;
        var storesDuringTheBackOff = -1;

        async Task BackOff(TimeSpan delay)
        {
            // The first compensation delete failed; the first upload still holds the fileKey while it waits to retry.
            second = Task.Run(() => Run(handler));
            await secondWaitingOrStoring.Task.WaitAsync(HangGuard);
            storesDuringTheBackOff = Count(handler, IsUsUpload);
        }

        var failure = await CaughtAsync(Task.Run(() => Run(handler, waitAsync: BackOff)));
        var created = await second!.WaitAsync(HangGuard);

        Assert.That(failure.Code, Is.EqualTo("UPSTREAM"));
        Assert.That(storesDuringTheBackOff, Is.EqualTo(1), "a store during the back-off would be deleted by the retried compensation");
        Assert.That(created.Created, Is.True);
        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[]
        {
            "by-sha1", "us-upload", "create", "by-sha1", "us-delete", // the first upload fails and its first delete fails
            "by-sha1", // the second upload's dedupe probe, before it waits for the fileKey
            "us-delete", // the first upload's retried compensation
            "us-upload", "create", // only now does the second upload store and create
        }));
        Assert.That(FileKeyLock.Count, Is.Zero);
    }

    [Test]
    public async Task AClientAbortWhileWaitingForANewMapsFileKey_PropagatesTheCancellation_AfterTheQuota_WithNothingStored()
    {
        using var aborted = new CancellationTokenSource();
        FileKeyLock = new TemporaryMapFileKeyLock { OnContended = _ => aborted.Cancel() };
        using var otherUpload = await FileKeyLock.AcquireAsync(FileKey, CancellationToken.None);
        var handler = StoredNewMapHandler();
        var limiter = new MintRateLimiter();
        var counts = new UploadCounts();

        Assert.CatchAsync<OperationCanceledException>(() => Run(handler, limiter, cancellationToken: aborted.Token).WaitAsync(HangGuard));

        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[] { "by-sha1" }), "waiting comes before the store");
        Assert.That(limiter.Count, Is.EqualTo(1), "the quota is checked before waiting for the fileKey");
        Assert.That(FileKeyLock.Count, Is.EqualTo(1), "the aborted waiter left nothing behind");
        counts.AssertNothingCounted();
    }

    [Test]
    public async Task AClientAbortWhileWaitingForARestoresFileKey_PropagatesTheCancellation_AfterTheProofIsVerified_WithNothingStored()
    {
        using var aborted = new CancellationTokenSource();
        FileKeyLock = new TemporaryMapFileKeyLock { OnContended = _ => aborted.Cancel() };
        using var otherRestore = await FileKeyLock.AcquireAsync(FileKey, CancellationToken.None);
        var handler = DeletedRecordHandler()
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(5811)))
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()));
        var counts = new UploadCounts();

        Assert.CatchAsync<OperationCanceledException>(() => Run(handler, cancellationToken: aborted.Token).WaitAsync(HangGuard));

        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[] { "by-sha1", "verify-proof" }));
        counts.AssertNothingCounted();
    }

    private static async Task<TemporaryMapUploadException> CaughtAsync(Task<TemporaryMapUploadOutcome> upload)
    {
        try
        {
            await upload.WaitAsync(HangGuard);
        }
        catch (TemporaryMapUploadException ex)
        {
            return ex;
        }

        Assert.Fail("the upload was expected to fail with a TemporaryMapUploadException");
        return null;
    }
}
