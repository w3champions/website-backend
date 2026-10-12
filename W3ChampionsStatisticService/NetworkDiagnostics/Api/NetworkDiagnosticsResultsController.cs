using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.NetworkDiagnostics.Submission;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Api;

/// <summary>
/// C5 launcher submit. The player authenticates with their own JWT (401 from the filter); the measurement token
/// travels in a header. The body is read by hand after the filter let the caller in: gzip is undone by the request
/// decompression middleware mounted for this path, the read stops one byte past the 8 MB cap, and the JSON is
/// snake_case (not MVC's [FromBody]). No [ApiController]: errors are always {"error": …} (Deviation 4).
/// </summary>
[Route("api/network-diagnostics")]
[Trace]
public class NetworkDiagnosticsResultsController(INetworkDiagnosticsSubmissionService submissions) : ControllerBase
{
    public const string TokenHeader = "X-Measurement-Token";

    /// <summary>
    /// Bound on the bytes on the wire (compressed). Gzip can make incompressible data slightly larger, so this sits
    /// a little above the decompressed cap; the decompressed cap itself is enforced by <see cref="SubmissionBodyReader"/>.
    /// </summary>
    public const long MaxWireBytes = SubmissionValidator.MaxBodyBytes + 64 * 1024;

    [HttpPost("results/{measurementId}")]
    [InjectActingPlayerAuthCode]
    [RequestSizeLimit(MaxWireBytes)]
    public async Task<IActionResult> Submit(
        string measurementId,
        string actingPlayer,
        [FromHeader(Name = TokenHeader)][NoTrace] string measurementToken,
        CancellationToken cancellationToken)
    {
        var (dto, error) = await SubmissionBodyReader.ReadAsync(Request.Body, SubmissionValidator.MaxBodyBytes, cancellationToken);
        if (error != null) return NetworkDiagnosticsAdminController.Map(NetDiagResult.Error(400, error));

        // After UseForwardedHeaders this is the client address the reverse proxy reported.
        var remoteIp = NetDiagIp.Ipv4Of(HttpContext.Connection.RemoteIpAddress);
        return NetworkDiagnosticsAdminController.Map(await submissions.SubmitAsync(measurementId, actingPlayer, measurementToken, dto, remoteIp));
    }
}
