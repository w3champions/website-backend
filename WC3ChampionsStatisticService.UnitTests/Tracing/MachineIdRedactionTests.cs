using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using W3ChampionsStatisticService.Services.Tracing;

namespace WC3ChampionsStatisticService.Tests.Tracing;

[TestFixture]
public class MachineIdRedactionTests
{
    private const string MachineId = "aa11bb22";

    [TestCase("/api/admin/bans/machine-ids/aa11bb22/accounts", "/api/admin/bans/machine-ids/REDACTED/accounts")]
    [TestCase("/API/Admin/Bans/Machine-Ids/aa11bb22/accounts", "/API/Admin/Bans/Machine-Ids/REDACTED/accounts")]
    [TestCase("/api/admin/bans/machine-ids/aa%2F11/accounts", "/api/admin/bans/machine-ids/REDACTED/accounts")]
    [TestCase("http://mm:3000/admin/bans/machine-ids/cc33dd44/accounts?excludeAccount=1001&machineAccess=true",
        "http://mm:3000/admin/bans/machine-ids/REDACTED/accounts?excludeAccount=1001&machineAccess=true")]
    [TestCase("GET /admin/bans/machine-ids/cc33dd44/accounts", "GET /admin/bans/machine-ids/REDACTED/accounts")]
    public void Redact_ReplacesTheMachineIdSegment(string value, string expected) =>
        Assert.That(MachineIdRedaction.Redact(value), Is.EqualTo(expected));

    [TestCase("/api/admin/bans/accounts/1001/machine-ids")]
    [TestCase("/api/admin/bans/machine-impact")]
    [TestCase("api/admin/bans/machine-ids/{machineId}/accounts")]   // the route template, not a value
    [TestCase("/api/players/Tester%231001")]
    [TestCase("")]
    [TestCase(null)]
    public void Redact_LeavesOtherValuesUnchanged(string value) =>
        Assert.That(MachineIdRedaction.Redact(value), Is.EqualTo(value));

    [Test]
    public void RedactTags_RewritesEveryStringTagAndTheDisplayName()
    {
        using var activity = new Activity("test");
        activity.DisplayName = "GET /api/admin/bans/machine-ids/aa11bb22/accounts";
        activity.SetTag("url.path", "/api/admin/bans/machine-ids/aa11bb22/accounts");
        activity.SetTag("url.full", "http://mm/admin/bans/machine-ids/aa11bb22/accounts?excludeAccount=1001");
        activity.SetTag("http.route", "api/admin/bans/machine-ids/{machineId}/accounts");
        activity.SetTag("http.response.status_code", 200);

        MachineIdRedaction.RedactTags(activity);

        Assert.That(activity.TagObjects.Select(t => t.Value?.ToString()), Has.None.Contains(MachineId));
        Assert.That(activity.DisplayName, Does.Not.Contain(MachineId));
        Assert.That(activity.GetTagItem("http.route"), Is.EqualTo("api/admin/bans/machine-ids/{machineId}/accounts"));
        Assert.That(activity.GetTagItem("http.response.status_code"), Is.EqualTo(200));
    }

    // The instrumentation options exactly as AddW3CTracing sets them, with the real ASP.NET Core and HttpClient
    // instrumentation: the incoming machine route makes the outgoing call to the matchmaking-like route.
    [Test]
    public async Task ExportedHttpSpans_NeverCarryTheMachineId()
    {
        var spans = new ConcurrentQueue<Activity>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddHttpClient();
        builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
            .SetSampler(new AlwaysOnSampler())
            .AddAspNetCoreInstrumentation(options => TracingServiceCollectionExtensions.ConfigureAspNetCore(options, "/hub"))
            .AddHttpClientInstrumentation(TracingServiceCollectionExtensions.ConfigureHttpClient)
            .AddProcessor(new Capture(spans)));
        await using var app = builder.Build();
        app.MapGet("/mm/admin/bans/machine-ids/{machineId}/accounts", () => "[]");
        app.MapGet("/api/admin/bans/machine-ids/{machineId}/accounts", async (string machineId, IHttpClientFactory factory, HttpContext context) =>
            await factory.CreateClient().GetStringAsync(
                $"{context.Request.Scheme}://{context.Request.Host}/mm/admin/bans/machine-ids/{machineId}/accounts?excludeAccount=1001"));
        await app.StartAsync();
        using var http = new HttpClient();   // not instrumented: created outside the app's service provider

        await http.GetStringAsync(app.Urls.Single() + "/api/admin/bans/machine-ids/" + MachineId + "/accounts");
        await app.StopAsync();

        var httpSpans = spans.Where(s => s.Kind is ActivityKind.Server or ActivityKind.Client).ToList();
        Assert.That(httpSpans.Select(s => s.Kind), Is.SupersetOf(new[] { ActivityKind.Server, ActivityKind.Client }));
        foreach (var span in httpSpans)
        {
            Assert.That(span.DisplayName, Does.Not.Contain(MachineId));
            Assert.That(span.TagObjects.Select(t => $"{t.Key}={t.Value}"), Has.None.Contains(MachineId));
        }
        Assert.That(httpSpans.Select(s => s.GetTagItem("url.path") ?? s.GetTagItem("url.full")),
            Has.Some.Contains("/machine-ids/REDACTED/accounts"));
    }

    [Test]
    public void LogEnricher_RedactsTheRequestPathScopeOfFrameworkLogs()
    {
        var events = new List<LogEvent>();
        var serilog = new Serilog.LoggerConfiguration()
            .Enrich.With(new MachineIdLogEnricher())
            .WriteTo.Sink(new ListSink(events))
            .CreateLogger();
        using var factory = new SerilogLoggerFactory(serilog);
        var logger = factory.CreateLogger("Microsoft.AspNetCore.Diagnostics");

        using (logger.BeginScope(new Dictionary<string, object> { ["RequestPath"] = "/api/admin/bans/machine-ids/aa11bb22/accounts" }))
        {
            logger.LogError("Request failed at {Path}", "/api/admin/bans/machine-ids/aa11bb22/accounts");
        }

        var rendered = events.Single().Properties.Select(p => $"{p.Key}={p.Value}").ToList();
        Assert.That(rendered, Has.Some.StartsWith("RequestPath="));
        Assert.That(rendered, Has.None.Contains(MachineId));
    }

    [Test]
    public void TelemetryInitializer_RedactsRequestAndDependencyTelemetry()
    {
        var initializer = new MachineIdTelemetryInitializer();
        var request = new RequestTelemetry { Name = "GET /api/admin/bans/machine-ids/aa11bb22/accounts", Url = new Uri("http://wb/api/admin/bans/machine-ids/aa11bb22/accounts") };
        var dependency = new DependencyTelemetry { Name = "GET /admin/bans/machine-ids/cc33dd44/accounts", Data = "http://mm/admin/bans/machine-ids/cc33dd44/accounts?excludeAccount=1001" };

        initializer.Initialize(request);
        initializer.Initialize(dependency);

        Assert.That(new[] { request.Name, request.Url.ToString(), dependency.Name, dependency.Data }, Has.None.Contains("aa11bb22").And.None.Contains("cc33dd44"));
        Assert.That(dependency.Data, Is.EqualTo("http://mm/admin/bans/machine-ids/REDACTED/accounts?excludeAccount=1001"));
    }

    private sealed class Capture(ConcurrentQueue<Activity> spans) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity activity) => spans.Enqueue(activity);
    }

    private sealed class ListSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
