using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace W3ChampionsStatisticService.Extensions;

public static class W3CLoggerConfiguration
{
    /// <summary>
    /// The configuration Program.cs builds the global Serilog logger from: website-backend's minimum levels, a JSON
    /// console sink for log scraping and a daily website-backend_yyyyMMdd.log file. The Microsoft.AspNetCore and
    /// System.Net.Http overrides also keep credentials out of the logs (spec §10.3): hosting's "Request starting" and
    /// IHttpClientFactory's "Sending HTTP request" Information entries carry full URLs (the hub's access_token query;
    /// the proofHash too before revision 10 moved it to the x-proof-hash header), Kestrel's bad-request entries quote
    /// the offending header line when its category is at Information, and IHttpClientFactory's Trace entries list the
    /// request headers. LogLevelOverrideTests pins those categories through the Microsoft.Extensions.Logging bridge
    /// the host uses.
    /// </summary>
    public static LoggerConfiguration Create() => new LoggerConfiguration()
        .WithW3CMinimumLevels()
        .WriteTo.Console(new JsonFormatter(renderMessage: true), restrictedToMinimumLevel: LogEventLevel.Information) // Write to Console to allow log scraping
        .WriteTo.File("Logs/website-backend_.log", rollingInterval: RollingInterval.Day);

    private static LoggerConfiguration WithW3CMinimumLevels(this LoggerConfiguration configuration) => configuration
        .MinimumLevel.Debug()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("AspNetCore.Authentication.Basic.BasicHandler", LogEventLevel.Warning) // Temporarily filter out the Basic auth schema log. We should add central JWT though.
        .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning) // Filter out verbose HTTP client logs
        .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning); // Filter out verbose System.Net.Http logs
}
