using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin.Jobs;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.Services.BackgroundTasks;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// D9: the self-provided map feature's registrations resolve from a ServiceCollection that holds the host's pre-existing
/// services as test doubles, so a wiring mistake fails here rather than at the first request after a deploy. The
/// AddIntercepted helpers build constructor arguments with GetService and pass null for anything unregistered, so
/// resolving alone proves little: the resolved upload service is also driven through one dedupe to show its clients are
/// really there, and the resolved sweep through one run for the same reason. The admin job is registered by AddAdminJobs
/// (the repo idiom for jobs), so it is resolved from that extension over these services.
/// </summary>
[TestFixture]
public class MapServiceExtensionsTests : TemporaryMapUploadServiceTestBase
{
    [Test]
    public async Task AddMapServices_ResolvesEveryRegistration_OverTheHostsPreExistingServices()
    {
        using var activitySource = new ActivitySource(nameof(MapServiceExtensionsTests));
        var handler = new ScriptedHttpHandler().On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811))).On(IsExpired, Respond(HttpStatusCode.OK, NoItems)).On(IsListing, Respond(HttpStatusCode.OK, NoFiles));
        var services = AddHostDoubles(new ServiceCollection(), handler, activitySource, new Mock<IW3CAuthenticationService>(MockBehavior.Strict).Object);

        services.AddMapServices();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var uploadService = provider.GetRequiredService<TemporaryMapUploadService>();
        Assert.That(provider.GetRequiredService<TemporaryMapUploadService>(), Is.SameAs(uploadService), "one upload service per process");
        var gate = provider.GetRequiredService<TemporaryMapUploadGate>();
        Assert.That(provider.GetRequiredService<TemporaryMapUploadGate>(), Is.SameAs(gate), "one gate per process, or the cap means nothing");
        var sweep = provider.GetRequiredService<TemporaryMapExpirySweep>();
        Assert.That(provider.GetRequiredService<TemporaryMapExpirySweep>(), Is.SameAs(sweep), "one sweep per process: its run lock is what serialises the daily and the admin-triggered runs");
        Assert.That(provider.GetServices<IHostedService>().Select(s => s.GetType()), Has.Member(typeof(TemporaryMapExpiryService)), "the daily trigger");

        var filter = new BearerRequiresPlayerAuthAttribute().CreateInstance(provider);
        Assert.That(filter, Is.InstanceOf<BearerRequiresPlayerAuthFilter>());
        Assert.That(filter.GetType(), Is.Not.EqualTo(typeof(BearerRequiresPlayerAuthFilter)), "a Castle class proxy, as registered before the move");
        Assert.That(new BearerRequiresPlayerAuthAttribute().CreateInstance(provider), Is.Not.SameAs(filter), "transient, as before the move");

        var (body, contentType) = BuildMultipart(Metadata(), "abc"u8.ToArray());
        var outcome = await uploadService.HandleUploadAsync(body, contentType, BattleTag, default);
        Assert.That(outcome.Response.MapId, Is.EqualTo(5811), "the resolved service reached the scripted matchmaking through its injected client");
        Assert.That(Count(handler, IsBySha1), Is.EqualTo(1));

        // A clock so far in the past that no spool file on this machine is stale by it: the resolved sweep purges the
        // machine-wide directory, and this only proves its two clients are there.
        var report = await sweep.RunOnceAsync(LongAgo, CancellationToken.None);
        Assert.That(Count(handler, IsExpired), Is.EqualTo(1), "the resolved sweep reached the scripted matchmaking");
        Assert.That(Count(handler, IsListing), Is.EqualTo(1), "and the scripted update-service");
        Assert.That(report.Failed, Is.Zero);
    }

    [Test]
    public async Task AddAdminJobs_RegistersTheExpiryJob_ResolvableOverTheMapServices()
    {
        // The job is a scoped IAdminJob the runner discovers from DI. AddAdminJobs' pre-existing registrations (the job
        // repository, the pressure probe) want a MongoClient this test has no business creating, so the collection is
        // not validated as a whole: the job is resolved and run, which is what the runner does.
        using var activitySource = new ActivitySource(nameof(MapServiceExtensionsTests));
        var handler = new ScriptedHttpHandler().On(IsExpired, Respond(HttpStatusCode.OK, NoItems)).On(IsListing, Respond(HttpStatusCode.OK, NoFiles));
        var services = AddHostDoubles(new ServiceCollection(), handler, activitySource, new Mock<IW3CAuthenticationService>(MockBehavior.Strict).Object)
            .AddMapServices()
            .AddAdminJobs();
        var context = new Mock<IAdminJobContext>();
        context.Setup(c => c.Report(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>(), null)).Returns(Task.CompletedTask);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var job = scope.ServiceProvider.GetServices<IAdminJob>().Single(j => j.Key == "temporary-maps-expiry");
        await job.RunAsync(context.Object, CancellationToken.None);

        Assert.That(job, Is.Not.TypeOf<TemporaryMapExpiryJob>(), "an interface proxy over the job, like every intercepted registration");
        Assert.That(Count(handler, IsExpired), Is.EqualTo(1), "the resolved job reached the sweep and its clients");
        Assert.That(Count(handler, IsListing), Is.EqualTo(1));
        context.Verify(c => c.Report(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>(), null), Times.Once);
    }

    private static readonly DateTime LongAgo = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const string NoItems = "{\"items\":[]}";
    private const string NoFiles = "{\"files\":[],\"next\":null}";

    private static bool IsExpired(HttpRequestMessage r)
        => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/maps/temporary/expired", StringComparison.Ordinal);

    private static bool IsListing(HttpRequestMessage r)
        => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/api/content/maps/files", StringComparison.Ordinal);

    [Test]
    public void AddMapServices_ReturnsTheCollection_ForChaining()
    {
        var services = new ServiceCollection();

        Assert.That(services.AddMapServices(), Is.SameAs(services));
    }
}
