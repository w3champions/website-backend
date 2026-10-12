using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.NetworkDiagnostics.Submission;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Api;

/// <summary>
/// C5 launcher submit. The player authenticates with their own JWT (401 from the filter); the measurement token
/// travels in a header. The body is read by hand, and only after the service checked owner, token and that the slot
/// is still open (a used or expired one answers 409 with the body unread): gzip is undone
/// by the request decompression middleware mounted for this path, the read stops one byte past the 8 MB cap, and the
/// JSON is snake_case (not MVC's [FromBody]). MVC itself never touches the body (no form binding).
/// No [ApiController]: errors are always {"error": …} (Deviation 4).
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
    [DisableFormValueModelBinding]
    [InjectActingPlayerAuthCode]
    [RequestSizeLimit(MaxWireBytes)]
    public async Task<IActionResult> Submit(
        [FromRoute] string measurementId,
        [FromHeader(Name = TokenHeader)][NoTrace] string measurementToken,
        CancellationToken cancellationToken)
    {
        // After UseForwardedHeaders this is the client address the reverse proxy reported.
        var remoteIp = NetDiagIp.PublicIpv4Of(HttpContext.Connection.RemoteIpAddress);
        // The service calls the reader only once the caller is known to own the measurement and to hold its token,
        // and the slot can still be submitted.
        var result = await submissions.SubmitAsync(
            measurementId,
            InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext).BattleTag,
            measurementToken,
            () => SubmissionBodyReader.ReadAsync(Request.Body, SubmissionValidator.MaxBodyBytes, cancellationToken),
            remoteIp);
        return NetworkDiagnosticsAdminController.Map(result);
    }
}
