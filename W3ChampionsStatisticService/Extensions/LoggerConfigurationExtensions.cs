using Serilog;
using Serilog.Events;

namespace W3ChampionsStatisticService.Extensions;

public static class LoggerConfigurationExtensions
{
    /// <summary>
    /// website-backend's minimum log levels. The Microsoft.AspNetCore and System.Net.Http overrides also keep proofHash
    /// values out of the logs (spec §10.3): hosting's "Request starting" and IHttpClientFactory's "Sending HTTP request"
    /// Information entries carry full URLs. LogLevelOverrideTests pins those categories.
    /// </summary>
    public static LoggerConfiguration WithW3CMinimumLevels(this LoggerConfiguration configuration) => configuration
        .MinimumLevel.Debug()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("AspNetCore.Authentication.Basic.BasicHandler", LogEventLevel.Warning) // Temporarily filter out the Basic auth schema log. We should add central JWT though.
        .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning) // Filter out verbose HTTP client logs
        .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning); // Filter out verbose System.Net.Http logs
}
