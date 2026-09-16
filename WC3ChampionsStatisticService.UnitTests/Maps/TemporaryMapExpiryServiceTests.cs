using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.MatchmakingService;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Admin.Jobs;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.Services.BackgroundTasks;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// The two triggers of <see cref="TemporaryMapExpirySweep"/>: the daily <see cref="TemporaryMapExpiryService"/> loop and
/// the on-demand <see cref="TemporaryMapExpiryJob"/>. The loop's interval is a seam, so the tests wait on signals from
/// the scripted handler, never on the clock.
/// </summary>
[TestFixture]
public class TemporaryMapExpiryServiceTests : TemporaryMapUploadServiceTestBase
{
    private const string Expired = "/maps/temporary/expired";
    private const string FileB = "W3Champions/CustomGames/b-22222222.w3x";

    [Test]
    public async Task StartsWithASweep_AndStopsPromptlyDuringTheDailyWait()
    {
        var swept = NewSignal();
        var handler = Idle(_ => swept.TrySetResult());
        var service = Service(handler);

        await service.StartAsync(CancellationToken.None);
        await swept.Task.WaitAsync(HangGuard);
        await service.StopAsync(new CancellationTokenSource(HangGuard).Token);

        Assert.That(service.ExecuteTask!.Status, Is.EqualTo(TaskStatus.RanToCompletion), "the daily wait ended with the stop, not with an exception");
        Assert.That(handler.CountRequests(HttpMethod.Get, Expired), Is.EqualTo(1));
    }

    [Test]
    public async Task SweepsAgainAfterTheInterval()
    {
        var sweptTwice = NewSignal();
        var handler = Idle(arrival =>
        {
            if (arrival == 2)
            {
                sweptTwice.TrySetResult();
            }
        });
        var service = Service(handler, TimeSpan.FromMilliseconds(10));

        await service.StartAsync(CancellationToken.None);
        await sweptTwice.Task.WaitAsync(HangGuard);
        await service.StopAsync(new CancellationTokenSource(HangGuard).Token);

        Assert.That(service.ExecuteTask!.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        Assert.That(handler.CountRequests(HttpMethod.Get, Expired), Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task ASweepThatThrows_IsLoggedAtError_AndTheLoopGoesOn()
    {
        // A sweep never throws by design (every pass isolates its failures), but a defect must not take the host down:
        // an unhandled exception in ExecuteAsync stops the whole application (BackgroundServiceExceptionBehavior.StopHost).
        using var logs = new LogCapture();
        var calledTwice = NewSignal();
        var sweep = new ThrowingSweep(new ScriptedHttpHandler(), calls =>
        {
            if (calls == 2)
            {
                calledTwice.TrySetResult();
            }
        });
        var service = new TemporaryMapExpiryService(sweep, logs.CreateLogger<TemporaryMapExpiryService>()) { Interval = TimeSpan.FromMilliseconds(10) };

        await service.StartAsync(CancellationToken.None);
        await calledTwice.Task.WaitAsync(HangGuard);
        await service.StopAsync(new CancellationTokenSource(HangGuard).Token);

        Assert.That(service.ExecuteTask!.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        var errors = logs.Lines().Where(l => l.StartsWith("Error", StringComparison.Ordinal) && l.Contains("sweep failed")).ToArray();
        Assert.That(errors, Has.Length.GreaterThanOrEqualTo(2), "one per failed run, and the loop went on");
        Assert.That(errors[0], Does.Contain("InvalidOperationException"), "the exception travels with the line");
    }

    [Test]
    public async Task StoppingDuringASweep_EndsTheLoopWithoutAnError()
    {
        using var logs = new LogCapture();
        var inListing = NewSignal();
        var release = NewSignal();
        var handler = new ScriptedHttpHandler()
            .OnAsync(r => r.Method == HttpMethod.Get && r.RequestUri!.PathAndQuery.Contains(Expired), async _ =>
            {
                inListing.TrySetResult();
                await release.Task.WaitAsync(HangGuard);
                return ScriptedHttpHandler.Json(HttpStatusCode.OK, "{\"items\":[{\"id\":1,\"path\":\"" + FileB + "\"}]}");
            })
            .On(HttpMethod.Delete, "/api/content/maps/file?", HttpStatusCode.NoContent, "")
            .On(HttpMethod.Post, "/file-deleted", HttpStatusCode.OK, "{\"map\":{\"id\":1}}")
            .On(HttpMethod.Get, "/api/content/maps/files", HttpStatusCode.OK, "{\"files\":[],\"next\":null}");
        var service = Service(handler, logger: logs.CreateLogger<TemporaryMapExpiryService>());

        await service.StartAsync(CancellationToken.None);
        await inListing.Task.WaitAsync(HangGuard);
        var stopping = service.StopAsync(new CancellationTokenSource(HangGuard).Token);
        release.TrySetResult();
        await stopping;

        Assert.That(service.ExecuteTask!.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        Assert.That(handler.CountRequests(HttpMethod.Delete, "/api/content/maps/file?"), Is.Zero, "the sweep stopped at the cancellation");
        Assert.That(logs.Lines(), Has.None.StartsWith("Error"));
    }

    [Test]
    public void TheDailyIntervalIsTheSpecs()
    {
        Assert.That(Service(new ScriptedHttpHandler()).Interval, Is.EqualTo(TimeSpan.FromHours(TemporaryMapLimits.SweepIntervalHours)));
    }

    [Test]
    public void TheAdminJobExposesTheSameSweepUnderAStableKey()
    {
        var job = new TemporaryMapExpiryJob(Sweep(new ScriptedHttpHandler()));

        Assert.That(job.Key, Is.EqualTo("temporary-maps-expiry"));
        Assert.That(job.Name, Is.Not.Empty);
        Assert.That(job.Description, Is.Not.Empty);
        Assert.That(job.RequiredPermission, Is.EqualTo(EPermission.Maps));
    }

    [Test]
    public async Task TheAdminJob_RunsTheSweepOnce_AndReportsItsCounts()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, Expired, HttpStatusCode.OK, "{\"items\":[{\"id\":1,\"path\":\"" + FileB + "\"}]}")
            .On(HttpMethod.Delete, "/api/content/maps/file?", HttpStatusCode.NoContent, "")
            .On(HttpMethod.Post, "/file-deleted", HttpStatusCode.OK, "{\"map\":{\"id\":1}}")
            .On(HttpMethod.Get, "/api/content/maps/files", HttpStatusCode.OK,
                "{\"files\":[{\"filePath\":\"W3Champions/CustomGames/c-33333333.w3x\"}],\"next\":null}")
            .On(HttpMethod.Get, "/maps/temporary/by-path", HttpStatusCode.NotFound);
        var context = new Mock<IAdminJobContext>(MockBehavior.Strict);
        context.Setup(c => c.AddItems(2));
        context.Setup(c => c.Report(2, 0, It.Is<string>(m =>
                m.Contains("deleted=1") && m.Contains("reclaimedOrphans=1") && m.Contains("failed=0") && m.Contains("purgedSpoolFiles=0")), null))
            .Returns(Task.CompletedTask);

        await new TemporaryMapExpiryJob(Sweep(handler)).RunAsync(context.Object, CancellationToken.None);

        context.VerifyAll();
        Assert.That(handler.CountRequests(HttpMethod.Get, Expired), Is.EqualTo(1));
        Assert.That(handler.CountRequests(HttpMethod.Delete, "/api/content/maps/file?"), Is.EqualTo(2), "the expired file and the orphan");
    }

    [Test]
    public void TheAdminJob_HandsItsCancellationToTheSweep()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var handler = new ScriptedHttpHandler();

        Assert.CatchAsync<OperationCanceledException>(
            () => new TemporaryMapExpiryJob(Sweep(handler)).RunAsync(new Mock<IAdminJobContext>(MockBehavior.Strict).Object, cancelled.Token));

        Assert.That(handler.Requests, Is.Empty);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    /// <summary>Nothing expired and nothing stored; <paramref name="onExpired"/> sees each expiry listing's ordinal.</summary>
    private static ScriptedHttpHandler Idle(Action<int> onExpired)
    {
        var arrivals = 0;
        return new ScriptedHttpHandler()
            .On(r => r.Method == HttpMethod.Get && r.RequestUri!.PathAndQuery.Contains(Expired), _ =>
            {
                onExpired(Interlocked.Increment(ref arrivals));
                return ScriptedHttpHandler.Json(HttpStatusCode.OK, "{\"items\":[]}");
            })
            .On(HttpMethod.Get, "/api/content/maps/files", HttpStatusCode.OK, "{\"files\":[],\"next\":null}");
    }

    private TemporaryMapExpirySweep Sweep(ScriptedHttpHandler handler)
    {
        var factory = new ScriptedHttpHandler.Factory(handler);
        return new TemporaryMapExpirySweep(
            new MatchmakingServiceClient(factory),
            new UpdateServiceClient(factory),
            NullLogger<TemporaryMapExpirySweep>.Instance)
        {
            SpoolDirectory = SpoolDirectory,
        };
    }

    /// <summary>The service over a sweep of <paramref name="handler"/>; without <paramref name="interval"/>, the production one.</summary>
    private TemporaryMapExpiryService Service(ScriptedHttpHandler handler, TimeSpan? interval = null, ILogger<TemporaryMapExpiryService> logger = null)
    {
        logger ??= NullLogger<TemporaryMapExpiryService>.Instance;
        return interval is { } every
            ? new TemporaryMapExpiryService(Sweep(handler), logger) { Interval = every }
            : new TemporaryMapExpiryService(Sweep(handler), logger);
    }

    /// <summary>A sweep with a defect: every run throws.</summary>
    private sealed class ThrowingSweep(ScriptedHttpHandler handler, Action<int> onCall)
        : TemporaryMapExpirySweep(
            new MatchmakingServiceClient(new ScriptedHttpHandler.Factory(handler)),
            new UpdateServiceClient(new ScriptedHttpHandler.Factory(handler)),
            NullLogger<TemporaryMapExpirySweep>.Instance)
    {
        private int _calls;

        public override Task<TemporaryMapSweepReport> RunOnceAsync(DateTime nowUtc, CancellationToken cancellationToken)
        {
            onCall(Interlocked.Increment(ref _calls));
            throw new InvalidOperationException("it broke");
        }
    }
}
