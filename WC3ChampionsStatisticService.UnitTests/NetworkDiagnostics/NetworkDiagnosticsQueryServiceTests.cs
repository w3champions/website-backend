using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.NetworkDiagnostics;
using W3ChampionsStatisticService.NetworkDiagnostics.Api;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;
using W3ChampionsStatisticService.NetworkDiagnostics.Submission;
using W3ChampionsStatisticService.Services.Interceptors;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class NetworkDiagnosticsQueryServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private Mock<INetworkDiagnosticsResultRepository> _results;
    private NetworkDiagnosticsQueryService _service;
    private BsonDocument _seenMeasurementFilter;
    private BsonDocument _seenObservationFilter;

    [SetUp]
    public void SetUp()
    {
        // Strict: a request answered with 400 or 404 must not have run any other query.
        _results = new Mock<INetworkDiagnosticsResultRepository>(MockBehavior.Strict);
        _seenMeasurementFilter = null;
        _seenObservationFilter = null;
        _results.Setup(r => r.ListMeasurementsAsync(It.IsAny<FilterDefinition<NetworkDiagnosticMeasurement>>(), It.IsAny<int>(), It.IsAny<int>()))
            .Callback<FilterDefinition<NetworkDiagnosticMeasurement>, int, int>((f, _, _) => _seenMeasurementFilter = NetDiagFilters.Render(f))
            .ReturnsAsync((new List<NetworkDiagnosticMeasurement>(), 0L));
        _service = new NetworkDiagnosticsQueryService(_results.Object);
    }

    private void ObservationLookupReturns(List<string> ids, bool truncated) =>
        _results.Setup(r => r.MeasurementIdsAsync(It.IsAny<FilterDefinition<NetworkDiagnosticObservation>>(), It.IsAny<int>()))
            .Callback<FilterDefinition<NetworkDiagnosticObservation>, int>((f, _) => _seenObservationFilter = NetDiagFilters.Render(f))
            .ReturnsAsync((ids, truncated));

    private static BuiltResult Built() => ResultBuilder.Build(
        SubmissionFixtures.Parse(SubmissionFixtures.FullJson),
        new NetworkDiagnosticAssignment { Id = SubmissionFixtures.MeasurementId, RunId = "run-1", BattleTag = "Name#1234" },
        Now, "81.2.69.160", new Dictionary<string, ClientGeo> { ["81.2.69.160"] = new() { Country = "GB" } });

    [Test]
    public async Task ListMeasurements_TargetFilter_ResolvesIdsFromObservationsFirst()
    {
        ObservationLookupReturns(["m1"], truncated: false);

        var result = await _service.ListMeasurementsAsync(new NetDiagQuery { TargetKey = "flo:12", Country = "DE" }, 0, 25);

        Assert.AreEqual(200, result.StatusCode);
        Assert.IsFalse(result.Truncated);
        // P6: the lookup is bounded by the one shared constant.
        _results.Verify(r => r.MeasurementIdsAsync(It.IsAny<FilterDefinition<NetworkDiagnosticObservation>>(), NetDiagConstants.MaxScannedObservations), Times.Once);
        Assert.AreEqual(200_000, NetDiagConstants.MaxScannedObservations);
        Assert.AreEqual("flo:12", _seenObservationFilter["TargetKey"].AsString);
        Assert.AreEqual("DE", _seenObservationFilter["Country"].AsString, "the client-level filters narrow the observation lookup too");
        Assert.AreEqual(new BsonArray { "m1" }, _seenMeasurementFilter["_id"]["$in"]);
        Assert.AreEqual("DE", _seenMeasurementFilter["ClientGeo.Country"].AsString);
    }

    // P6: when the observation scan stopped at its limit, older matches may be missing and the answer says so.
    [Test]
    public async Task ListMeasurements_ObservationLookupHitTheLimit_IsFlaggedTruncated()
    {
        ObservationLookupReturns(["m1"], truncated: true);

        var result = await _service.ListMeasurementsAsync(new NetDiagQuery { Method = "TcpPing" }, 0, 25);

        Assert.AreEqual(200, result.StatusCode);
        Assert.IsTrue(result.Truncated);
    }

    [Test]
    public async Task ListMeasurements_NoTargetFilter_SkipsTheObservationLookup()
    {
        var result = await _service.ListMeasurementsAsync(new NetDiagQuery { Country = "DE" }, 0, 25);

        Assert.IsFalse(result.Truncated);
        Assert.IsFalse(_seenMeasurementFilter.Contains("_id"));
        _results.Verify(r => r.MeasurementIdsAsync(It.IsAny<FilterDefinition<NetworkDiagnosticObservation>>(), It.IsAny<int>()), Times.Never);
    }

    // A5: page is zero-based and reaches the repository unchanged (the repository skips page * pageSize).
    [Test]
    public async Task ListMeasurements_PageIsZeroBased_AndPassedThrough()
    {
        Assert.AreEqual(200, (await _service.ListMeasurementsAsync(new NetDiagQuery(), 0, 25)).StatusCode);
        await _service.ListMeasurementsAsync(new NetDiagQuery(), 2, 25);

        _results.Verify(r => r.ListMeasurementsAsync(It.IsAny<FilterDefinition<NetworkDiagnosticMeasurement>>(), 0, 25), Times.Once);
        _results.Verify(r => r.ListMeasurementsAsync(It.IsAny<FilterDefinition<NetworkDiagnosticMeasurement>>(), 2, 25), Times.Once);
    }

    [Test]
    public async Task ListMeasurements_MapsThePage()
    {
        _results.Setup(r => r.ListMeasurementsAsync(It.IsAny<FilterDefinition<NetworkDiagnosticMeasurement>>(), 0, 100))
            .ReturnsAsync((new List<NetworkDiagnosticMeasurement> { Built().Measurement }, 41L));

        var page = (PageDto<MeasurementSummaryDto>)(await _service.ListMeasurementsAsync(null, 0, 100)).Body;

        Assert.AreEqual(41, page.Total);
        Assert.AreEqual(SubmissionFixtures.MeasurementId, page.Items.Single().MeasurementId);
        StringAssert.StartsWith("""{"items":[{"measurementId":""", JsonSerializer.Serialize(page, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [TestCase(0, 101, "pageSize must be in 1-100")]
    [TestCase(0, 0, "pageSize must be in 1-100")]
    [TestCase(-1, 10, "page must be >= 0")]
    [TestCase(int.MaxValue, 100, "page is too large")]
    public async Task ListMeasurements_BadPaging_400_BeforeAnyQuery(int page, int pageSize, string error)
    {
        var result = await _service.ListMeasurementsAsync(new NetDiagQuery { TargetKey = "flo:12" }, page, pageSize);

        Assert.AreEqual(400, result.StatusCode);
        Assert.AreEqual(error, JsonSerializer.SerializeToElement(result.Body).GetProperty("error").GetString());
        _results.VerifyNoOtherCalls();
    }

    [Test]
    public async Task GetMeasurement_Missing_404()
    {
        _results.Setup(r => r.GetMeasurementAsync("nope")).ReturnsAsync((NetworkDiagnosticMeasurement)null);

        var result = await _service.GetMeasurementAsync("nope", raw: true);

        Assert.AreEqual(404, result.StatusCode);
        Assert.AreEqual("""{"error":"not_found"}""", JsonSerializer.Serialize(result.Body));
    }

    [Test]
    public async Task GetMeasurement_WithoutRaw_NeverLoadsTheRawSeries_AndAsksIpInfoForTheRunsHops()
    {
        var built = Built();
        IReadOnlyCollection<string> seenIps = null;
        _results.Setup(r => r.GetMeasurementAsync(built.Measurement.Id)).ReturnsAsync(built.Measurement);
        _results.Setup(r => r.IpInfoAsync("run-1", It.IsAny<IReadOnlyCollection<string>>()))
            .Callback<string, IReadOnlyCollection<string>>((_, ips) => seenIps = ips)
            .ReturnsAsync([new NetworkDiagnosticIpInfo { RunId = "run-1", Ip = "1.2.3.4", AsnOrg = "Cloudflare" }]);

        var result = await _service.GetMeasurementAsync(built.Measurement.Id, raw: false);

        var detail = (MeasurementDetailDto)result.Body;
        Assert.AreEqual(200, result.StatusCode);
        Assert.AreEqual("Cloudflare", detail.IpInfo["1.2.3.4"].AsnOrg);
        Assert.IsNull(detail.RawExpired);
        Assert.IsNull(detail.Targets[0].Paths[0].Probes[1].Samples);
        CollectionAssert.AreEquivalent(new[] { "81.2.69.160", "192.168.1.1", "1.128.0.1", "1.2.3.4" }, seenIps);
        _results.Verify(r => r.GetRawSeriesAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task GetMeasurement_WithRaw_AddsTheSeries()
    {
        var built = Built();
        _results.Setup(r => r.GetMeasurementAsync(built.Measurement.Id)).ReturnsAsync(built.Measurement);
        _results.Setup(r => r.GetRawSeriesAsync(built.Measurement.Id)).ReturnsAsync(built.RawSeries);
        _results.Setup(r => r.IpInfoAsync("run-1", It.IsAny<IReadOnlyCollection<string>>())).ReturnsAsync([]);

        var detail = (MeasurementDetailDto)(await _service.GetMeasurementAsync(built.Measurement.Id, raw: true)).Body;

        Assert.AreEqual(2, detail.Targets[0].Paths[0].Probes[1].Samples.Count);
        Assert.IsNull(detail.RawExpired);
    }

    // A4: the raw series live 90 days, the measurement 365.
    [Test]
    public async Task GetMeasurement_WithRaw_AfterTheRawSeriesExpired_SaysSo()
    {
        var built = Built();
        _results.Setup(r => r.GetMeasurementAsync(built.Measurement.Id)).ReturnsAsync(built.Measurement);
        _results.Setup(r => r.GetRawSeriesAsync(built.Measurement.Id)).ReturnsAsync((NetworkDiagnosticRawSeries)null);
        _results.Setup(r => r.IpInfoAsync("run-1", It.IsAny<IReadOnlyCollection<string>>())).ReturnsAsync([]);

        var detail = (MeasurementDetailDto)(await _service.GetMeasurementAsync(built.Measurement.Id, raw: true)).Body;

        Assert.AreEqual(true, detail.RawExpired);
    }

    // A measurement without any IP (no client IP, no hops) needs no IpInfo query.
    [Test]
    public async Task GetMeasurement_WithoutAnyIp_SkipsTheIpInfoQuery()
    {
        _results.Setup(r => r.GetMeasurementAsync("m1")).ReturnsAsync(new NetworkDiagnosticMeasurement { Id = "m1", RunId = "run-1" });

        var detail = (MeasurementDetailDto)(await _service.GetMeasurementAsync("m1", raw: false)).Body;

        Assert.IsEmpty(detail.IpInfo);
    }

    // The filter set names a player and IPs; like the stored result (P13) it stays out of trace tags. The tracing
    // proxy reads the attribute from the interface method.
    [Test]
    public void ListMeasurementsAsync_QueryParameter_IsNoTraceOnTheInterface()
    {
        var parameter = typeof(INetworkDiagnosticsQueryService)
            .GetMethod(nameof(INetworkDiagnosticsQueryService.ListMeasurementsAsync))!
            .GetParameters().Single(p => p.ParameterType == typeof(NetDiagQuery));

        Assert.IsNotNull(parameter.GetCustomAttribute<NoTraceAttribute>());
    }

    // A10 / Review Focus 7: nothing these endpoints return or take has a token-like property, at any depth.
    [TestCase(typeof(MeasurementSummaryDto))]
    [TestCase(typeof(MeasurementDetailDto))]
    [TestCase(typeof(NetDiagQuery))]
    public void QueryTypes_HaveNoTokenProperty(Type root)
    {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>([root]);
        while (pending.Count > 0)
        {
            var type = pending.Pop();
            if (!seen.Add(type) || type.Namespace?.StartsWith("W3ChampionsStatisticService", StringComparison.Ordinal) != true) continue;
            foreach (var property in type.GetProperties())
            {
                StringAssert.DoesNotContain("token", property.Name.ToLowerInvariant(), $"{type.Name}.{property.Name}");
                pending.Push(property.PropertyType);
                foreach (var argument in property.PropertyType.IsGenericType ? property.PropertyType.GetGenericArguments() : []) pending.Push(argument);
            }
        }
        CollectionAssert.Contains(seen, root);
    }
}

[TestFixture]
public class NetworkDiagnosticsQueryRegistrationTests
{
    // The read side handles no token, so it runs behind the tracing proxy; what the proxy records of a call must
    // still leave out the filter set (a player's BattleTag, IPs).
    [Test]
    public async Task AddNetworkDiagnostics_ResolvesTheTracedQueryService_AndTheFilterSetStaysOutOfTheSpan()
    {
        using var source = new ActivitySource("netdiag-query-registration-test");
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var results = new Mock<INetworkDiagnosticsResultRepository>();
        results.Setup(r => r.ListMeasurementsAsync(It.IsAny<FilterDefinition<NetworkDiagnosticMeasurement>>(), 0, 25))
            .ReturnsAsync((new List<NetworkDiagnosticMeasurement>(), 0L));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNetworkDiagnostics();
        services.AddSingleton(new TracingInterceptor(source));
        services.AddSingleton(results.Object);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var service = provider.GetRequiredService<INetworkDiagnosticsQueryService>();
        var result = await service.ListMeasurementsAsync(new NetDiagQuery { BattleTag = "Name#1234", TargetIp = "" }, 0, 25);

        Assert.AreEqual(200, result.StatusCode);
        Assert.AreNotEqual(typeof(NetworkDiagnosticsQueryService), service.GetType(), "resolved through the tracing proxy");
        var span = spans.Single(s => s.OperationName == "NetworkDiagnosticsQueryService.ListMeasurementsAsync");
        CollectionAssert.AreEquivalent(new[] { "param.page", "param.pageSize" }, span.Tags.Select(t => t.Key));
        Assert.IsFalse(span.Tags.Any(t => t.Value?.Contains("Name#1234") == true));
    }
}
