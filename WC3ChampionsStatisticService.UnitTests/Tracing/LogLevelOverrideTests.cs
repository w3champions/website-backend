using NUnit.Framework;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using W3ChampionsStatisticService.Extensions;

namespace WC3ChampionsStatisticService.Tests.Tracing;

/// <summary>
/// Hosting's "Request starting" and IHttpClientFactory's "Sending HTTP request" entries are logged at Information
/// and carry full URLs, so a proofHash (spec §10.3). The level overrides Program.cs applies must keep those
/// categories at Warning; lowering any of them fails here.
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
        using var logger = new LoggerConfiguration().WithW3CMinimumLevels().CreateLogger();

        var categoryLogger = logger.ForContext(Constants.SourceContextPropertyName, category);

        Assert.That(categoryLogger.IsEnabled(LogEventLevel.Information), Is.False);
        Assert.That(categoryLogger.IsEnabled(LogEventLevel.Warning), Is.True);
    }

    [Test]
    public void ApplicationCategories_KeepTheDebugMinimum()
    {
        using var logger = new LoggerConfiguration().WithW3CMinimumLevels().CreateLogger();

        var categoryLogger = logger.ForContext(Constants.SourceContextPropertyName, "W3ChampionsStatisticService.Maps.MapsController");

        Assert.That(categoryLogger.IsEnabled(LogEventLevel.Debug), Is.True);
        Assert.That(categoryLogger.IsEnabled(LogEventLevel.Verbose), Is.False);
    }
}
