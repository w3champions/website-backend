using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Servers;
using NUnit.Framework;
using W3ChampionsStatisticService.Services.Tracing;
using WC3ChampionsStatisticService.Tests.Maps;

namespace WC3ChampionsStatisticService.Tests.Tracing;

[TestFixture]
public class TelemetryRedactionTests
{
    private const string Hash = TemporaryMapClientTests.ProofHash;
    private const string Sha1 = TemporaryMapClientTests.Sha1;

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

    private static CommandStartedEvent Command(string name, BsonDocument command)
        => new(name, command, new DatabaseNamespace("w3c-telemetry-redaction-tests"), null, 1,
            new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017))));
}
