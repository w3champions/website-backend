using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace W3ChampionsStatisticService.NetworkDiagnostics;

public static class NetworkDiagnosticsApplicationBuilderExtensions
{
    private static readonly PathString ResultsPath = new("/api/network-diagnostics/results");

    /// <summary>
    /// Undoes Content-Encoding: gzip for the C5 result submission only; no other endpoint accepts compressed bodies.
    /// The middleware only wraps the body stream: nothing is inflated until the action reads, and that read is capped.
    /// </summary>
    public static IApplicationBuilder UseNetworkDiagnosticsRequestDecompression(this IApplicationBuilder app) =>
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(ResultsPath),
            branch => branch.UseRequestDecompression());
}
