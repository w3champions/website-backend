using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics.Api;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

/// <summary>
/// The C6 measurement endpoints over real HTTP: routing, MVC query binding of the shared filter set and JSON output.
/// The permission filter (it needs a signed admin JWT) is replaced by a stand-in that does the one thing that
/// matters for binding: it overwrites the "battleTag" action argument with the acting admin. The requests are the
/// ones the website builds (NetworkDiagnosticsService.ts, measurementFilterParams).
/// </summary>
[TestFixture]
public class NetworkDiagnosticsAdminQueryEndpointTests
{
    private const string ActingAdmin = "Admin#1";

    private WebApplication _app;
    private HttpClient _http;
    private Mock<INetworkDiagnosticsQueryService> _queries;
    private NetDiagQuery _seenQuery;
    private (int Page, int PageSize) _seenPaging;

    [OneTimeSetUp]
    public async Task StartServer()
    {
        _queries = new Mock<INetworkDiagnosticsQueryService>();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddControllers(options => options.Conventions.Add(new ActingAdminStandIn()))
            .AddApplicationPart(typeof(NetworkDiagnosticsAdminController).Assembly)
            .ConfigureApplicationPartManager(parts =>
            {
                parts.FeatureProviders.Clear();
                parts.FeatureProviders.Add(new OnlyTheAdminController());
            });
        builder.Services.AddSingleton(Mock.Of<INetworkDiagnosticsRunService>());
        builder.Services.AddSingleton(_queries.Object);

        _app = builder.Build();
        _app.UseRouting();
        _app.MapControllers();
        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.Single()) };
    }

    [OneTimeTearDown]
    public async Task StopServer()
    {
        _http?.Dispose();
        if (_app != null) await _app.DisposeAsync();
    }

    [SetUp]
    public void SetUp()
    {
        _queries.Reset();
        _seenQuery = null;
        _seenPaging = default;
        _queries.Setup(q => q.ListMeasurementsAsync(It.IsAny<NetDiagQuery>(), It.IsAny<int>(), It.IsAny<int>()))
            .Callback<NetDiagQuery, int, int>((query, page, pageSize) => (_seenQuery, _seenPaging) = (query, (page, pageSize)))
            .ReturnsAsync(NetDiagResult.Ok(new PageDto<MeasurementSummaryDto>([], 0)));
    }

    private async Task<(int Status, string Body, HttpResponseMessage Response)> Get(string pathAndQuery)
    {
        var response = await _http.GetAsync(pathAndQuery);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(), response);
    }

    [Test]
    public async Task ListMeasurements_BindsEverySharedFilter_AsTheWebsiteSendsThem()
    {
        var (status, body, _) = await Get(
            "/api/network-diagnostics/measurements?battleTag=Name%231234&runId=r1&runId=r2&measurementId=m1&measurementId=m2"
            + "&targetKey=flo%3A12&nodeId=12&targetIp=1.2.3.4&path=direct&method=IcmpMtr&country=DE&city=Berlin&asn=3320"
            + "&os=windows&adapterType=wifi&vpn=false&from=2026-10-01T00%3A00%3A00.000Z&to=2026-10-02T00%3A00%3A00.000Z&page=2&pageSize=50");

        Assert.AreEqual((200, """{"items":[],"total":0}"""), (status, body));
        // The filtered player is the one in the query string, not the acting admin the permission filter injects.
        Assert.AreEqual("Name#1234", _seenQuery.BattleTag);
        Assert.AreEqual(new[] { "r1", "r2" }, _seenQuery.RunId);
        Assert.AreEqual(new[] { "m1", "m2" }, _seenQuery.MeasurementId);
        Assert.AreEqual(("flo:12", 12, "1.2.3.4", "direct", "IcmpMtr"), (_seenQuery.TargetKey, _seenQuery.NodeId!.Value, _seenQuery.TargetIp, _seenQuery.Path, _seenQuery.Method));
        Assert.AreEqual(("DE", "Berlin", 3320L, "windows", "wifi", false), (_seenQuery.Country, _seenQuery.City, _seenQuery.Asn!.Value, _seenQuery.Os, _seenQuery.AdapterType, _seenQuery.Vpn!.Value));
        Assert.AreEqual((2, 50), _seenPaging);

        // A5: the instants are the UTC ones that were sent, whatever the server's time zone.
        var window = NetDiagFilters.Render(NetDiagFilters.Measurements(_seenQuery, []))["SubmittedAt"];
        Assert.AreEqual(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), window["$gte"].ToUniversalTime());
        Assert.AreEqual(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), window["$lt"].ToUniversalTime());
    }

    [Test]
    public async Task ListMeasurements_NoQuery_IsAnEmptyFilterOnTheFirstPage()
    {
        var (status, _, response) = await Get("/api/network-diagnostics/measurements");

        Assert.AreEqual(200, status);
        Assert.AreEqual(new NetDiagQuery(), _seenQuery with { RunId = null, MeasurementId = null });
        Assert.That(_seenQuery.RunId, Is.Null.Or.Empty);
        Assert.That(_seenQuery.MeasurementId, Is.Null.Or.Empty);
        Assert.AreEqual((0, 25), _seenPaging);
        Assert.IsFalse(response.Headers.Contains("X-NetDiag-Truncated"));
    }

    // An offset-less instant is UTC (A5), and vpn=true binds.
    [Test]
    public async Task ListMeasurements_InstantWithoutOffset_IsTakenAsUtc()
    {
        await Get("/api/network-diagnostics/measurements?from=2026-10-01T00:00:00&vpn=true");

        Assert.AreEqual(true, _seenQuery.Vpn);
        Assert.AreEqual(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            NetDiagFilters.Render(NetDiagFilters.Observations(_seenQuery))["SubmittedAt"]["$gte"].ToUniversalTime());
    }

    // Empty values are what a cleared form field sends: they are no filter and no error.
    [Test]
    public async Task ListMeasurements_EmptyValues_AreNoFilterAndNoError()
    {
        var (status, _, _) = await Get("/api/network-diagnostics/measurements?battleTag=&nodeId=&asn=&vpn=&from=&to=&targetKey=");

        Assert.AreEqual(200, status);
        Assert.AreEqual(new NetDiagQuery(), _seenQuery with { RunId = null, MeasurementId = null });
    }

    // P8: a value that does not parse answers 400 naming the query parameter; the service is not asked.
    [TestCase("nodeId=abc", "nodeId")]
    [TestCase("nodeId=99999999999", "nodeId")]
    [TestCase("asn=x", "asn")]
    [TestCase("vpn=maybe", "vpn")]
    [TestCase("from=yesterday", "from")]
    [TestCase("to=2026-13-45", "to")]
    [TestCase("page=x", "page")]
    [TestCase("pageSize=1.5", "pageSize")]
    [TestCase("NODEID=abc", "nodeId")]
    [TestCase("to=bad&asn=bad", "asn")]
    public async Task ListMeasurements_UnparsableValue_400NamingTheParameter(string query, string key)
    {
        var (status, body, _) = await Get($"/api/network-diagnostics/measurements?{query}");

        Assert.AreEqual((400, $$"""{"error":"invalid query parameter: {{key}}"}"""), (status, body));
        _queries.Verify(q => q.ListMeasurementsAsync(It.IsAny<NetDiagQuery>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task ListMeasurements_TruncatedLookup_SendsTheHeader()
    {
        _queries.Setup(q => q.ListMeasurementsAsync(It.IsAny<NetDiagQuery>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(NetDiagResult.Ok(new PageDto<MeasurementSummaryDto>([], 0)) with { Truncated = true });

        var (status, body, response) = await Get("/api/network-diagnostics/measurements?method=TcpPing");

        Assert.AreEqual((200, """{"items":[],"total":0}"""), (status, body));
        Assert.AreEqual("true", response.Headers.GetValues("X-NetDiag-Truncated").Single());
    }

    [TestCase("", false)]
    [TestCase("?raw=false", false)]
    [TestCase("?raw=true", true)]
    public async Task GetMeasurement_RoutesTheIdAndRaw(string query, bool raw)
    {
        _queries.Setup(q => q.GetMeasurementAsync("6f1d0c9e-4c1b-4a8e-9d55-0b6b8f0f7a11", raw)).ReturnsAsync(NetDiagResult.Ok(new { measurementId = "x" }));

        var (status, body, _) = await Get($"/api/network-diagnostics/measurements/6f1d0c9e-4c1b-4a8e-9d55-0b6b8f0f7a11{query}");

        Assert.AreEqual((200, """{"measurementId":"x"}"""), (status, body));
    }

    [Test]
    public async Task GetMeasurement_UnparsableRaw_400NamingTheParameter()
    {
        var (status, body, _) = await Get("/api/network-diagnostics/measurements/m1?raw=maybe");

        Assert.AreEqual((400, """{"error":"invalid query parameter: raw"}"""), (status, body));
        _queries.Verify(q => q.GetMeasurementAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task GetMeasurement_ServiceError_KeepsStatusAndErrorShape()
    {
        _queries.Setup(q => q.GetMeasurementAsync("nope", false)).ReturnsAsync(NetDiagResult.Error(404, "not_found"));

        var (status, body, _) = await Get("/api/network-diagnostics/measurements/nope");

        Assert.AreEqual((404, """{"error":"not_found"}"""), (status, body));
    }

    private sealed class OnlyTheAdminController : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo) => typeInfo.AsType() == typeof(NetworkDiagnosticsAdminController);
    }

    /// <summary>Swaps BearerHasPermissionFilter for a filter with its one side effect on the action arguments.</summary>
    private sealed class ActingAdminStandIn : IActionModelConvention
    {
        public void Apply(ActionModel action)
        {
            var permissionFilters = action.Filters.OfType<BearerHasPermissionFilter>().ToList();
            Assert.AreEqual(1, permissionFilters.Count, action.ActionName);
            action.Filters.Remove(permissionFilters[0]);
            action.Filters.Add(new InjectActingAdmin());
        }
    }

    private sealed class InjectActingAdmin : IActionFilter
    {
        // Same statement as BearerHasPermissionFilter.OnActionExecutionAsync.
        public void OnActionExecuting(ActionExecutingContext context) => context.ActionArguments["battleTag"] = ActingAdmin;

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}
