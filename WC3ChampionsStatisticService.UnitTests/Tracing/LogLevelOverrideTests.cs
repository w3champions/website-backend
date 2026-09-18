using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Serilog.Extensions.Logging;
using W3ChampionsStatisticService.Extensions;

namespace WC3ChampionsStatisticService.Tests.Tracing;

/// <summary>
/// Hosting's "Request starting" and IHttpClientFactory's "Sending HTTP request" entries are logged at Information
/// and carry full URLs, so a credential sent as a query value (spec §10.3); Kestrel quotes a rejected request's
/// header line in its bad-request entries once its category is at Information, so a malformed x-proof-hash header
/// (revision 10 moved the proofHash there) would be logged with its value. The configuration Program.cs builds its
/// logger from must keep those categories at Warning; lowering any of them fails here. The check goes through the
/// Microsoft.Extensions.Logging bridge that <c>UseSerilog()</c> installs, whose <see cref="ILogger.IsEnabled"/> is
/// the gate every entry passes before it is forwarded; nothing is emitted, so the configuration's console and file
/// sinks never write.
/// </summary>
[TestFixture]
public class LogLevelOverrideTests
{
    [TestCase("Microsoft.AspNetCore")]
    [TestCase("Microsoft.AspNetCore.Hosting.Diagnostics")]
    [TestCase("Microsoft.AspNetCore.Server.Kestrel")]
    [TestCase("Microsoft.AspNetCore.Server.Kestrel.BadRequests")]
    [TestCase("System.Net.Http")]
    [TestCase("System.Net.Http.HttpClient")]
    [TestCase("System.Net.Http.HttpClient.Default.LogicalHandler")]
    [TestCase("System.Net.Http.HttpClient.Default.ClientHandler")]
    public void CredentialCarryingCategories_LogWarningsButNotInformation(string category)
    {
        using var serilogLogger = W3CLoggerConfiguration.Create().CreateLogger();
        using var loggerFactory = new SerilogLoggerFactory(serilogLogger);
        var logger = loggerFactory.CreateLogger(category);

        Assert.That(logger.IsEnabled(LogLevel.Information), Is.False, "an Information entry of this category would carry a full URL or a header line");
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
}
