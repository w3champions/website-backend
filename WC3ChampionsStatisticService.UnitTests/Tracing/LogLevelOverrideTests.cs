using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using W3ChampionsStatisticService.Extensions;
using WC3ChampionsStatisticService.Tests.Maps;

namespace WC3ChampionsStatisticService.Tests.Tracing;

/// <summary>
/// Hosting's "Request starting" and IHttpClientFactory's "Sending HTTP request" entries are logged at Information
/// and carry full URLs, so a proofHash (spec §10.3). The configuration Program.cs builds its logger from must keep
/// those categories at Warning; lowering any of them fails here. Entries go through the Microsoft.Extensions.Logging
/// bridge that <c>UseSerilog()</c> installs, and only filtered or unemitted levels are used, so the configuration's
/// console and file sinks never write.
/// </summary>
[TestFixture]
public class LogLevelOverrideTests
{
    [TestCase("Microsoft.AspNetCore")]
    [TestCase("Microsoft.AspNetCore.Hosting.Diagnostics")]
    [TestCase("System.Net.Http")]
    [TestCase("System.Net.Http.HttpClient")]
    [TestCase("System.Net.Http.HttpClient.Default.LogicalHandler")]
    [TestCase("System.Net.Http.HttpClient.Default.ClientHandler")]
    public void UrlCarryingCategories_LogWarningsButNotInformation(string category)
    {
        var sink = new CollectingSink();
        using var serilogLogger = W3CLoggerConfiguration.Create().WriteTo.Sink(sink).CreateLogger();
        using var loggerFactory = new SerilogLoggerFactory(serilogLogger);
        var logger = loggerFactory.CreateLogger(category);

        logger.LogInformation("Request starting HTTP/1.1 GET {Url}",
            "https://website-backend.test/api/maps/temporary/status?proofHash=" + TemporaryMapClientTests.ProofHash);

        Assert.That(sink.Events, Is.Empty, "an Information entry of this category reached the sinks");
        Assert.That(logger.IsEnabled(LogLevel.Information), Is.False);
        Assert.That(logger.IsEnabled(LogLevel.Warning), Is.True);
    }

    [Test]
    public void ApplicationCategories_KeepTheDebugMinimum()
    {
        using var serilogLogger = W3CLoggerConfiguration.Create().CreateLogger();
        using var loggerFactory = new SerilogLoggerFactory(serilogLogger);
        var logger = loggerFactory.CreateLogger("W3ChampionsStatisticService.Maps.MapsController");

        Assert.That(logger.IsEnabled(LogLevel.Debug), Is.True);
        Assert.That(logger.IsEnabled(LogLevel.Trace), Is.False);
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
}
