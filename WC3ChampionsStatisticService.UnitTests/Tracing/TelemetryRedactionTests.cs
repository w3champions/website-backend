using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Servers;
using NUnit.Framework;
using W3C.Domain.Maps;
using W3ChampionsStatisticService.Services.Tracing;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using WC3ChampionsStatisticService.Tests.Maps;

namespace WC3ChampionsStatisticService.Tests.Tracing;

[TestFixture]
public class TelemetryRedactionTests
{
    private const string Hash = TemporaryMapClientTests.ProofHash;
    private const string Sha1 = TemporaryMapClientTests.Sha1;
    private const string HubToken = "raw-hub-token-under-test";

    /// <summary>Placeholders for the outbound credentials; never the real values.</summary>
    internal const string OutboundSecret = "placeholder-admin-secret-under-test";
    internal const string OutboundJwt = "placeholder.caller-jwt.under-test";

    [TestCase("https://mm.test/maps/temporary/by-proof-hash/HASH", "https://mm.test/maps/temporary/by-proof-hash/Redacted")]
    [TestCase("/maps/temporary/by-proof-hash/HASH", "/maps/temporary/by-proof-hash/Redacted")]
    [TestCase("https://mm.test/api/maps/temporary/by-proof-hash/HASH?trace=1", "https://mm.test/api/maps/temporary/by-proof-hash/Redacted?trace=1")]
    [TestCase("https://mm.test/MAPS/Temporary/By-Proof-Hash/HASH/", "https://mm.test/MAPS/Temporary/By-Proof-Hash/Redacted/")]
    [TestCase("GET /maps/temporary/by-proof-hash/HASH", "GET /maps/temporary/by-proof-hash/Redacted")]
    [TestCase("https://wb.test/api/maps/temporary/status?proofHash=HASH", "https://wb.test/api/maps/temporary/status?proofHash=Redacted")]
    [TestCase("https://wb.test/api/maps/temporary/status?a=1&PROOFhash=HASH&b=2", "https://wb.test/api/maps/temporary/status?a=1&PROOFhash=Redacted&b=2")]
    [TestCase("https://wb.test/x?proof%48ash=HASH", "https://wb.test/x?proof%48ash=Redacted")]
    [TestCase("https://wb.test/x?proofHash=HASH&proofHash=HASH", "https://wb.test/x?proofHash=Redacted&proofHash=Redacted")]
    [TestCase("https://wb.test/x?mapProof=HASH#part", "https://wb.test/x?mapProof=Redacted#part")]
    public void RedactUrl_RedactsTheProofHashPathSegmentAndSecretQueryValues(string input, string expected)
    {
        var redacted = TelemetryRedaction.RedactUrl(input.Replace("HASH", Hash));

        Assert.That(redacted, Is.EqualTo(expected));
    }

    [TestCase("https://wb.test/websiteBackendHub?access_token=TOKEN", "https://wb.test/websiteBackendHub?access_token=Redacted")]
    [TestCase("https://wb.test/websiteBackendHub/negotiate?negotiateVersion=1&access_token=TOKEN&id=abc",
        "https://wb.test/websiteBackendHub/negotiate?negotiateVersion=1&access_token=Redacted&id=abc")]
    [TestCase("/websiteBackendHub?Access_Token=TOKEN", "/websiteBackendHub?Access_Token=Redacted")]
    public void RedactUrl_RedactsTheHubAccessToken(string input, string expected)
    {
        // The SignalR hub takes its JWT or ticket as ?access_token= (browsers cannot set headers on WebSockets).
        Assert.That(TelemetryRedaction.RedactUrl(input.Replace("TOKEN", HubToken)), Is.EqualTo(expected));
    }

    [TestCase("https://replay.test/generate/42?secret=SECRET", "https://replay.test/generate/42?secret=Redacted")]
    [TestCase("https://identity.test/api/permissions?id=Peter%23123&Authorization=JWT", "https://identity.test/api/permissions?id=Peter%23123&Authorization=Redacted")]
    public void RedactUrl_RedactsOutboundCredentialKeys(string input, string expected)
    {
        // ReplayServiceClient sends the admin secret as ?secret= and IdentityServiceClient the caller's JWT as
        // ?authorization=. The HttpClient instrumentation's own query redaction can be switched off by configuration.
        var url = input.Replace("SECRET", OutboundSecret).Replace("JWT", OutboundJwt);

        Assert.That(TelemetryRedaction.RedactUrl(url), Is.EqualTo(expected));
    }

    [TestCase("https://replay.test/generate/42?secret=SECRET", "https://replay.test/generate/42?secret=Redacted")]
    [TestCase("https://replay.test/chats/42?secret=SECRET", "https://replay.test/chats/42?secret=Redacted")]
    [TestCase("https://identity.test/api/permissions?authorization=JWT", "https://identity.test/api/permissions?authorization=Redacted")]
    [TestCase("https://identity.test/api/permissions?id=Peter%23123&authorization=JWT", "https://identity.test/api/permissions?id=Redacted&authorization=Redacted")]
    [TestCase("https://any.test/x?season=22&gateway=20", "https://any.test/x?season=Redacted&gateway=Redacted")]
    [TestCase("https://any.test/x?flag&a=1#part", "https://any.test/x?flag&a=Redacted#part")]
    [TestCase("https://any.test/x?a=&b=Redacted", "https://any.test/x?a=&b=Redacted")]
    [TestCase("https://mm.test/maps/temporary/by-proof-hash/HASH?trace=1", "https://mm.test/maps/temporary/by-proof-hash/Redacted?trace=Redacted")]
    public void RedactUrlQueryValues_RedactsEveryValue_AndKeepsTheKeys(string input, string expected)
    {
        var url = input.Replace("SECRET", OutboundSecret).Replace("JWT", OutboundJwt).Replace("HASH", Hash);

        Assert.That(TelemetryRedaction.RedactUrlQueryValues(url), Is.EqualTo(expected));
    }

    [TestCase("https://mm.test/maps/temporary/by-sha1/SHA1")]
    [TestCase("https://any.test/x?")]
    [TestCase("")]
    [TestCase(null)]
    public void RedactUrlQueryValues_LeavesUrlsWithoutValuesUntouched(string input)
    {
        var url = input?.Replace("SHA1", Sha1);

        Assert.That(TelemetryRedaction.RedactUrlQueryValues(url), Is.SameAs(url));
    }

    [TestCase("https://mm.test/maps/temporary/by-sha1/SHA1")]
    [TestCase("https://mm.test/maps/temporary/by-path?path=W3Champions%2FCustomGames%2FLegion%20TD-94ec3bda.w3x")]
    [TestCase("https://wb.test/api/maps?filter=proofHash&offset=0")]
    [TestCase("https://wb.test/x?notproofHash=SHA1&proofHashes=SHA1")]
    [TestCase("https://wb.test/x?proofHash")]
    [TestCase("https://wb.test/x?proofHash=")]
    [TestCase("https://wb.test/x?proofHash=Redacted")]
    [TestCase("https://mm.test/maps/temporary/by-proof-hash/")]
    [TestCase("https://mm.test/maps/temporary/by-proof-hash-cache/SHA1")]
    [TestCase("https://wb.test/x#proofHash=SHA1")]
    [TestCase("")]
    [TestCase(null)]
    public void RedactUrl_LeavesEverythingElseUntouched(string input)
    {
        var url = input?.Replace("SHA1", Sha1);

        Assert.That(TelemetryRedaction.RedactUrl(url), Is.SameAs(url));
    }

    [TestCase("?proofHash=HASH", "?proofHash=Redacted")]
    [TestCase("proofHash=HASH&season=22", "proofHash=Redacted&season=22")]
    [TestCase("?season=22&mapproof=HASH", "?season=22&mapproof=Redacted")]
    public void RedactQuery_RedactsSecretValuesWithOrWithoutTheLeadingQuestionMark(string input, string expected)
    {
        Assert.That(TelemetryRedaction.RedactQuery(input.Replace("HASH", Hash)), Is.EqualTo(expected));
    }

    [TestCase("?season=22&gateway=20")]
    [TestCase("maps/temporary/by-proof-hash/SHA1")]
    [TestCase(null)]
    public void RedactQuery_LeavesOtherQueriesUntouched(string input)
    {
        var query = input?.Replace("SHA1", Sha1);

        Assert.That(TelemetryRedaction.RedactQuery(query), Is.SameAs(query));
    }

    [Test]
    public void Processor_RedactsEveryUrlTagBeforeExport()
    {
        using var activity = new Activity("GET").Start();
        activity.SetTag("url.full", $"https://mm.test/maps/temporary/by-proof-hash/{Hash}");
        activity.SetTag("url.path", $"/maps/temporary/by-proof-hash/{Hash}");
        activity.SetTag("url.query", $"?proofHash={Hash}");
        activity.SetTag("http.route", "api/maps/temporary/status");
        activity.Stop();

        new TelemetryRedactionProcessor().OnEnd(activity);

        Assert.That(activity.GetTagItem("url.full"), Is.EqualTo("https://mm.test/maps/temporary/by-proof-hash/Redacted"));
        Assert.That(activity.GetTagItem("url.path"), Is.EqualTo("/maps/temporary/by-proof-hash/Redacted"));
        Assert.That(activity.GetTagItem("url.query"), Is.EqualTo("?proofHash=Redacted"));
        Assert.That(activity.GetTagItem("http.route"), Is.EqualTo("api/maps/temporary/status"));
        Assert.That(activity.TagObjects.Any(t => t.Value?.ToString()?.Contains(Hash) == true), Is.False);
    }

    [Test]
    public void Processor_LeavesOtherSpansUntouched()
    {
        using var activity = new Activity("GET").Start();
        var urlFull = $"https://mm.test/maps/temporary/by-sha1/{Sha1}";
        activity.SetTag("url.full", urlFull);
        activity.SetTag("url.query", 42);
        activity.Stop();

        new TelemetryRedactionProcessor().OnEnd(activity);

        Assert.That(activity.GetTagItem("url.full"), Is.SameAs(urlFull));
        Assert.That(activity.GetTagItem("url.query"), Is.EqualTo(42));
    }

    [TestCase("x-proof-hash")]
    [TestCase("X-Proof-Hash")]
    [TestCase("X-PROOF-HASH")]
    [TestCase("x_proof_hash")]
    [TestCase("authorization")]
    [TestCase("Authorization")]
    [TestCase("proxy-authorization")]
    [TestCase("cookie")]
    [TestCase("Set-Cookie")]
    [TestCase("x-admin-secret")]
    [TestCase("X-Admin-Secret")]
    [TestCase("x-map-key")]
    [TestCase("x-api-token")]
    [TestCase("X-API-Token")]
    [TestCase(ChatServiceSecretAuthFilter.HeaderName)]
    public void IsSecretHeader_RecognisesEveryCredentialHeader_WhateverItsCase(string name)
    {
        // Headers are case-insensitive on the wire, and the OpenTelemetry semantic conventions spelled them with
        // underscores before dashes.
        Assert.That(TelemetryRedaction.IsSecretHeader(name), Is.True);
    }

    [TestCase("content-type")]
    [TestCase("x-faro-session-id")]
    [TestCase("x-forwarded-for")]
    [TestCase("user-agent")]
    [TestCase("x-proof-hash-version")]
    [TestCase("proofhash")]
    [TestCase("x-proof")]
    [TestCase("")]
    [TestCase(null)]
    public void IsSecretHeader_LeavesEveryOtherHeaderAlone(string name)
    {
        Assert.That(TelemetryRedaction.IsSecretHeader(name), Is.False);
    }

    [TestCase("http.request.header.x-proof-hash")]
    [TestCase("http.request.header.x_proof_hash")]
    [TestCase("http.request.header.authorization")]
    [TestCase("http.response.header.set-cookie")]
    [TestCase("HTTP.Request.Header.X-Proof-Hash")]
    [TestCase("x-proof-hash")]
    public void IsSecretHeaderKey_RecognisesTheHeaderTagNamesOfTheSemanticConventions(string key)
    {
        // Instrumentations that record headers name the attribute http.request.header.<name> or
        // http.response.header.<name>; a property dump may use the bare header name.
        Assert.That(TelemetryRedaction.IsSecretHeaderKey(key), Is.True);
    }

    [TestCase("http.request.header.content-type")]
    [TestCase("http.request.header.x-faro-session-id")]
    [TestCase("http.request.header.")]
    [TestCase("http.request.header")]
    [TestCase("http.request.header.x-proof-hash.first")]
    [TestCase("url.full")]
    [TestCase("proofHash")]
    [TestCase("")]
    [TestCase(null)]
    public void IsSecretHeaderKey_LeavesEveryOtherKeyAlone(string key)
    {
        Assert.That(TelemetryRedaction.IsSecretHeaderKey(key), Is.False);
    }

    [Test]
    public void Processor_RedactsSecretHeaderTags_AndKeepsTheOthers()
    {
        // No instrumentation records headers today; should an enrich hook or a future option do so, the value of
        // a credential header never reaches the exporter (spec §10.3: the x-proof-hash header is never logged).
        using var activity = new Activity("GET").Start();
        activity.SetTag("http.request.header.x-proof-hash", Hash);
        activity.SetTag("http.request.header.authorization", "Bearer " + HubToken);
        activity.SetTag("http.request.header.x_admin_secret", OutboundSecret);
        activity.SetTag("http.response.header.set-cookie", "session=" + HubToken);
        activity.SetTag("http.request.header.x-faro-session-id", "session-42");
        activity.SetTag("http.request.header.content-type", "application/json");
        activity.SetTag("http.route", "api/maps/temporary/status");
        activity.Stop();

        new TelemetryRedactionProcessor().OnEnd(activity);

        Assert.That(activity.GetTagItem("http.request.header.x-proof-hash"), Is.EqualTo(TelemetryRedaction.Redacted));
        Assert.That(activity.GetTagItem("http.request.header.authorization"), Is.EqualTo(TelemetryRedaction.Redacted));
        Assert.That(activity.GetTagItem("http.request.header.x_admin_secret"), Is.EqualTo(TelemetryRedaction.Redacted));
        Assert.That(activity.GetTagItem("http.response.header.set-cookie"), Is.EqualTo(TelemetryRedaction.Redacted));
        Assert.That(activity.GetTagItem("http.request.header.x-faro-session-id"), Is.EqualTo("session-42"));
        Assert.That(activity.GetTagItem("http.request.header.content-type"), Is.EqualTo("application/json"));
        Assert.That(activity.GetTagItem("http.route"), Is.EqualTo("api/maps/temporary/status"));
        var values = activity.TagObjects.Select(t => t.Value?.ToString()).ToList();
        Assert.That(values.Any(v => v?.Contains(Hash) == true || v?.Contains(HubToken) == true || v?.Contains(OutboundSecret) == true), Is.False);
    }

    [Test]
    public void Processor_RedactsAMultiValuedSecretHeaderTag()
    {
        // The semantic conventions record header values as arrays; two x-proof-hash lines arrive as one such tag.
        using var activity = new Activity("GET").Start();
        activity.SetTag("http.request.header.x-proof-hash", new[] { Hash, Hash });
        activity.Stop();

        new TelemetryRedactionProcessor().OnEnd(activity);

        Assert.That(activity.GetTagItem("http.request.header.x-proof-hash"), Is.EqualTo(TelemetryRedaction.Redacted));
    }

    [Test]
    public void MongoPredicate_SkipsOnlyApiTokenCommands()
    {
        var options = TracingServiceCollectionExtensions.CreateMongoInstrumentationOptions();

        Assert.That(options.CaptureCommandText, Is.True);
        Assert.That(options.ShouldStartActivity(Command("find", new BsonDocument("find", "ApiToken"))), Is.False);
        Assert.That(options.ShouldStartActivity(Command("update", new BsonDocument("update", "ApiToken"))), Is.False);
        Assert.That(options.ShouldStartActivity(Command("insert", new BsonDocument("insert", "ApiToken"))), Is.False);
        Assert.That(options.ShouldStartActivity(Command("delete", new BsonDocument("delete", "ApiToken"))), Is.False);
        Assert.That(options.ShouldStartActivity(Command("getMore", new BsonDocument { { "getMore", 42L }, { "collection", "ApiToken" } })), Is.False);
        Assert.That(options.ShouldStartActivity(Command("find", new BsonDocument("find", "ApiTokenAudit"))), Is.True);
        Assert.That(options.ShouldStartActivity(Command("find", new BsonDocument("find", "apitoken"))), Is.True,
            "Mongo collection names are case-sensitive; only the ApiToken collection holds raw tokens");
        Assert.That(options.ShouldStartActivity(Command("find", new BsonDocument("find", "Clan"))), Is.True);
        Assert.That(options.ShouldStartActivity(Command("ping", new BsonDocument("ping", 1))), Is.True);
    }

    [Test]
    public void AppInsightsInitializer_RedactsRequestUrlsAndDependencyUrls()
    {
        var request = new RequestTelemetry
        {
            Name = "GET TemporaryMaps/GetStatus",
            Url = new Uri($"https://wb.test/api/maps/temporary/status?proofHash={Hash}"),
        };
        var dependency = new DependencyTelemetry
        {
            Type = "Http",
            Target = "mm.test",
            Name = $"GET /maps/temporary/by-proof-hash/{Hash}",
            Data = $"https://mm.test/maps/temporary/by-proof-hash/{Hash}",
        };
        var initializer = new TelemetryRedactionInitializer();

        initializer.Initialize(request);
        initializer.Initialize(dependency);

        Assert.That(request.Url.OriginalString, Is.EqualTo("https://wb.test/api/maps/temporary/status?proofHash=Redacted"));
        Assert.That(request.Name, Is.EqualTo("GET TemporaryMaps/GetStatus"));
        Assert.That(dependency.Name, Is.EqualTo("GET /maps/temporary/by-proof-hash/Redacted"));
        Assert.That(dependency.Data, Is.EqualTo("https://mm.test/maps/temporary/by-proof-hash/Redacted"));
    }

    [Test]
    public void AppInsightsInitializer_RedactsSecretHeaderProperties_OnEveryKindOfTelemetry()
    {
        // No initializer or module of this service copies headers into telemetry properties; should one do so,
        // under the semantic-convention tag name or the bare header name, the credential value never leaves.
        var request = new RequestTelemetry { Name = "GET TemporaryMaps/GetStatus" };
        request.Properties["http.request.header.x-proof-hash"] = Hash;
        request.Properties[TemporaryMapKeys.ProofHashHeaderName] = Hash;
        request.Properties["http.request.header.authorization"] = "Bearer " + HubToken;
        request.Properties["http.request.header.x-faro-session-id"] = "session-42";
        var dependency = new DependencyTelemetry { Type = "Http", Name = "POST /maps/temporary/by-proof-hash" };
        dependency.Properties["http.request.header.x-admin-secret"] = OutboundSecret;
        dependency.Properties["http.response.header.content-type"] = "application/json";
        var trace = new TraceTelemetry("message");
        trace.Properties["X-Proof-Hash"] = Hash;
        var initializer = new TelemetryRedactionInitializer();

        initializer.Initialize(request);
        initializer.Initialize(dependency);
        initializer.Initialize(trace);

        Assert.That(request.Properties["http.request.header.x-proof-hash"], Is.EqualTo(TelemetryRedaction.Redacted));
        Assert.That(request.Properties[TemporaryMapKeys.ProofHashHeaderName], Is.EqualTo(TelemetryRedaction.Redacted));
        Assert.That(request.Properties["http.request.header.authorization"], Is.EqualTo(TelemetryRedaction.Redacted));
        Assert.That(request.Properties["http.request.header.x-faro-session-id"], Is.EqualTo("session-42"));
        Assert.That(request.Name, Is.EqualTo("GET TemporaryMaps/GetStatus"));
        Assert.That(dependency.Properties["http.request.header.x-admin-secret"], Is.EqualTo(TelemetryRedaction.Redacted));
        Assert.That(dependency.Properties["http.response.header.content-type"], Is.EqualTo("application/json"));
        Assert.That(dependency.Name, Is.EqualTo("POST /maps/temporary/by-proof-hash"));
        Assert.That(trace.Properties["X-Proof-Hash"], Is.EqualTo(TelemetryRedaction.Redacted));
        Assert.That(trace.Message, Is.EqualTo("message"));
    }

    [Test]
    public void AppInsightsDependency_RedactsEveryQueryValueOfTheOutboundUrl()
    {
        // The HTTP dependency collector stores the full request URI in Data and "METHOD /path" in Name.
        var replay = new DependencyTelemetry
        {
            Type = "Http",
            Target = "replay.test",
            Name = "GET /generate/42",
            Data = $"https://replay.test/generate/42?secret={OutboundSecret}",
        };
        var identity = new DependencyTelemetry
        {
            Type = "Http",
            Target = "identity.test",
            Name = "DELETE /api/permissions",
            Data = $"https://identity.test/api/permissions?id=Peter%23123&authorization={OutboundJwt}",
        };
        var initializer = new TelemetryRedactionInitializer();

        initializer.Initialize(replay);
        initializer.Initialize(identity);

        Assert.That(replay.Data, Is.EqualTo("https://replay.test/generate/42?secret=Redacted"));
        Assert.That(identity.Data, Is.EqualTo("https://identity.test/api/permissions?id=Redacted&authorization=Redacted"));
        Assert.That(replay.Name, Is.EqualTo("GET /generate/42"));
        Assert.That(identity.Name, Is.EqualTo("DELETE /api/permissions"));
        Assert.That(identity.Target, Is.EqualTo("identity.test"));
    }

    [TestCase("Http", "http://identity.test/api/permissions?id=Peter%23123&authorization=JWT",
        "http://identity.test/api/permissions?id=Redacted&authorization=Redacted")]
    [TestCase("Http (tracked component)", "https://mm.test/maps?filter=legion&includeTemporary=true",
        "https://mm.test/maps?filter=Redacted&includeTemporary=Redacted")]
    [TestCase("WCF Service", "https://legacy.test/Service.svc?id=7&secret=SECRET", "https://legacy.test/Service.svc?id=Redacted&secret=Redacted")]
    [TestCase("Azure blob", "https://account.blob.test/replays/42?sv=2020&sig=SIGNATURE", "https://account.blob.test/replays/42?sv=Redacted&sig=Redacted")]
    [TestCase("Http", "HTTPS://mm.test/maps?filter=legion", "HTTPS://mm.test/maps?filter=Redacted")]
    [TestCase("Http", " https://mm.test/maps?filter=legion&id=7", " https://mm.test/maps?filter=Redacted&id=Redacted")]
    [TestCase("Http", "\thttp://mm.test/maps?filter=legion", "\thttp://mm.test/maps?filter=Redacted")]
    [TestCase("Http", "\uFEFFhttps://mm.test/maps?filter=legion", "\uFEFFhttps://mm.test/maps?filter=Redacted")]
    [TestCase("Http", " \uFEFF https://mm.test/maps?filter=legion", " \uFEFF https://mm.test/maps?filter=Redacted")]
    public void AppInsightsHttpDependency_RedactsEveryQueryValue_WhateverTypeApplicationInsightsGaveIt(string type, string data, string expected)
    {
        // Application Insights' HttpDependenciesParsingTelemetryInitializer runs before this one and renames some HTTP
        // dependencies ("WCF Service", "Azure blob", ...), keeping the request URL in Data. HttpClient accepts a
        // request URL with leading whitespace (a misconfigured base URL) and the collector records it verbatim, so
        // the URL is still recognised behind leading whitespace or a byte-order mark.
        var dependency = new DependencyTelemetry
        {
            Type = type,
            Name = "GET /x",
            Data = data.Replace("SECRET", OutboundSecret).Replace("JWT", OutboundJwt),
        };

        new TelemetryRedactionInitializer().Initialize(dependency);

        Assert.That(dependency.Data, Is.EqualTo(expected));
        Assert.That(dependency.Type, Is.EqualTo(type));
    }

    [TestCase("SQL", "SELECT name FROM maps WHERE note = 'a?season=22&gateway=20'", "SELECT name FROM maps WHERE note = 'a?season=22&gateway=20'")]
    [TestCase("InProc", "find maps ?season=22&gateway=20", "find maps ?season=22&gateway=20")]
    [TestCase("InProc", "  find maps ?season=22&secret=SECRET", "  find maps ?season=22&secret=Redacted")]
    [TestCase("InProc", "replay export 42?season=22&secret=SECRET", "replay export 42?season=22&secret=Redacted")]
    [TestCase(null, "generate/42?season=22&authorization=JWT", "generate/42?season=22&authorization=Redacted")]
    public void AppInsightsNonHttpDependency_KeepsItsData_ExceptSecretValues(string type, string data, string expected)
    {
        // Data that is not an HTTP URL (a command, a query text) is never rewritten wholesale; the values of the
        // credential keys and proofHash segments are still redacted, whatever the dependency is.
        var dependency = new DependencyTelemetry
        {
            Type = type,
            Name = "command",
            Data = data.Replace("SECRET", OutboundSecret).Replace("JWT", OutboundJwt),
        };

        new TelemetryRedactionInitializer().Initialize(dependency);

        Assert.That(dependency.Data, Is.EqualTo(expected));
    }

    [Test]
    public void AddW3CApplicationInsights_RedactsEveryQueryValue_AfterApplicationInsightsRenamesAnHttpDependency()
    {
        var channel = new CollectingTelemetryChannel();
        var services = new ServiceCollection();
        services.AddSingleton<ITelemetryChannel>(channel);
#pragma warning disable CS0618 // Application Insights 2.23 resolves the obsolete IHostingEnvironment the web host provides.
        services.AddSingleton<IHostingEnvironment>(new TestHostingEnvironment());
#pragma warning restore CS0618
        services.AddW3CApplicationInsights("00000000-0000-0000-0000-000000000000");
        // Collection modules and sampling only; the telemetry initializers stay exactly as registered. None of the
        // modules may start background work or reach the network from a unit test.
        services.Configure<ApplicationInsightsServiceOptions>(o =>
        {
            o.EnableQuickPulseMetricStream = false;
            o.EnablePerformanceCounterCollectionModule = false;
            o.EnableAppServicesHeartbeatTelemetryModule = false;
            o.EnableAzureInstanceMetadataTelemetryModule = false;
            o.EnableDependencyTrackingTelemetryModule = false;
            o.EnableRequestTrackingTelemetryModule = false;
            o.EnableEventCounterCollectionModule = false;
            o.EnableDiagnosticsTelemetryModule = false;
            o.EnableHeartbeat = false;
            o.EnableAdaptiveSampling = false;
            o.AddAutoCollectedMetricExtractor = false;
        });
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<TelemetryClient>();

        client.TrackDependency(new DependencyTelemetry
        {
            Type = "Http",
            Target = "legacy.test",
            Name = "GET /Service.svc",
            Data = $"https://legacy.test/Service.svc?id=7&secret={OutboundSecret}",
        });

        var sent = channel.Items.OfType<DependencyTelemetry>().Single();
        Assert.That(sent.Type, Is.EqualTo("WCF Service"), "Application Insights' own parsing initializer must have run first");
        Assert.That(sent.Data, Is.EqualTo("https://legacy.test/Service.svc?id=Redacted&secret=Redacted"));
    }

    [Test]
    public void HubAccessToken_IsRedactedFromAppInsightsRequestsAndSpanUrls()
    {
        var request = new RequestTelemetry { Url = new Uri($"https://wb.test/websiteBackendHub?id=abc&access_token={HubToken}") };
        using var activity = new Activity("GET").Start();
        activity.SetTag("url.full", $"https://wb.test/websiteBackendHub/negotiate?negotiateVersion=1&access_token={HubToken}");
        activity.SetTag("url.query", $"?negotiateVersion=1&access_token={HubToken}");
        activity.Stop();

        new TelemetryRedactionInitializer().Initialize(request);
        new TelemetryRedactionProcessor().OnEnd(activity);

        Assert.That(request.Url.OriginalString, Is.EqualTo("https://wb.test/websiteBackendHub?id=abc&access_token=Redacted"));
        Assert.That(activity.GetTagItem("url.full"), Is.EqualTo("https://wb.test/websiteBackendHub/negotiate?negotiateVersion=1&access_token=Redacted"));
        Assert.That(activity.GetTagItem("url.query"), Is.EqualTo("?negotiateVersion=1&access_token=Redacted"));
    }

    [Test]
    public void AppInsightsInitializer_LeavesOtherTelemetryUntouched()
    {
        var url = new Uri($"https://wb.test/api/maps/temporary/by-sha1/{Sha1}");
        var request = new RequestTelemetry { Url = url };
        var initializer = new TelemetryRedactionInitializer();

        initializer.Initialize(request);
        initializer.Initialize(new RequestTelemetry());
        initializer.Initialize(new DependencyTelemetry());
        initializer.Initialize(new TraceTelemetry("message"));

        Assert.That(request.Url, Is.SameAs(url));
    }

    [Test]
    public void AddW3CApplicationInsights_RegistersTheRedactionInitializer()
    {
        var services = new ServiceCollection();

        services.AddW3CApplicationInsights("00000000-0000-0000-0000-000000000000");

        Assert.That(services.Any(d => d.ServiceType == typeof(ITelemetryInitializer)
                                      && d.ImplementationType == typeof(TelemetryRedactionInitializer)), Is.True);
        Assert.That(services.Any(d => d.ServiceType == typeof(TelemetryConfiguration)), Is.True,
            "Application Insights itself must still be registered");
    }

#pragma warning disable CS0618 // See AddW3CApplicationInsights_RedactsEveryQueryValue_AfterApplicationInsightsRenamesAnHttpDependency.
    private sealed class TestHostingEnvironment : IHostingEnvironment
#pragma warning restore CS0618
    {
        public string EnvironmentName { get; set; } = "Test";

        public string ApplicationName { get; set; } = "website-backend-tests";

        public string WebRootPath { get; set; }

        public IFileProvider WebRootFileProvider { get; set; }

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; }
    }

    private static CommandStartedEvent Command(string name, BsonDocument command)
        => new(name, command, new DatabaseNamespace("w3c-telemetry-redaction-tests"), null, 1,
            new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017))));
}
