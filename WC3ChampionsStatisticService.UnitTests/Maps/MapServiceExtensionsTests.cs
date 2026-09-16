using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// D9: the self-provided map feature's registrations resolve from a ServiceCollection that holds the host's pre-existing
/// services as test doubles, so a wiring mistake fails here rather than at the first request after a deploy. The
/// AddIntercepted helpers build constructor arguments with GetService and pass null for anything unregistered, so
/// resolving alone proves little: the resolved upload service is also driven through one dedupe to show its clients are
/// really there. Task 6 extends this fixture with the sweep, the hosted service and the admin job.
/// </summary>
[TestFixture]
public class MapServiceExtensionsTests : TemporaryMapUploadServiceTestBase
{
    [Test]
    public async Task AddMapServices_ResolvesEveryRegistration_OverTheHostsPreExistingServices()
    {
        using var activitySource = new ActivitySource(nameof(MapServiceExtensionsTests));
        var handler = new ScriptedHttpHandler().On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811)));
        var services = AddHostDoubles(new ServiceCollection(), handler, activitySource, new Mock<IW3CAuthenticationService>(MockBehavior.Strict).Object);

        services.AddMapServices();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var uploadService = provider.GetRequiredService<TemporaryMapUploadService>();
        Assert.That(provider.GetRequiredService<TemporaryMapUploadService>(), Is.SameAs(uploadService), "one upload service per process");
        var gate = provider.GetRequiredService<TemporaryMapUploadGate>();
        Assert.That(provider.GetRequiredService<TemporaryMapUploadGate>(), Is.SameAs(gate), "one gate per process, or the cap means nothing");

        var filter = new BearerRequiresPlayerAuthAttribute().CreateInstance(provider);
        Assert.That(filter, Is.InstanceOf<BearerRequiresPlayerAuthFilter>());
        Assert.That(filter.GetType(), Is.Not.EqualTo(typeof(BearerRequiresPlayerAuthFilter)), "a Castle class proxy, as registered before the move");
        Assert.That(new BearerRequiresPlayerAuthAttribute().CreateInstance(provider), Is.Not.SameAs(filter), "transient, as before the move");

        var (body, contentType) = BuildMultipart(Metadata(), "abc"u8.ToArray());
        var outcome = await uploadService.HandleUploadAsync(body, contentType, BattleTag, default);
        Assert.That(outcome.Response.MapId, Is.EqualTo(5811), "the resolved service reached the scripted matchmaking through its injected client");
        Assert.That(Count(handler, IsBySha1), Is.EqualTo(1));
    }

    [Test]
    public void AddMapServices_ReturnsTheCollection_ForChaining()
    {
        var services = new ServiceCollection();

        Assert.That(services.AddMapServices(), Is.SameAs(services));
    }
}
