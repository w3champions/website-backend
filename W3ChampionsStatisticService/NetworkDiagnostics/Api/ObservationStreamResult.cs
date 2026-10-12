using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Api;

/// <summary>
/// Writes observation rows as csv or ndjson while they are read from the database: one batch of the server cursor is
/// in memory at a time. The cursor's first batch was read before this result exists, so a query that fails or times
/// out at once is still answered with an ordinary error. A failure after that finds the status line and earlier rows
/// already sent; the connection is then aborted, so the client sees a broken transfer (HTTP/1.1: no terminating
/// chunk; HTTP/2: stream reset) and never a short body that ended normally.
/// </summary>
public sealed class ObservationStreamResult(IAsyncCursor<NetworkDiagnosticObservation> rows, ObservationFormat format) : IActionResult
{
    public async Task ExecuteResultAsync(ActionContext context)
    {
        var http = context.HttpContext;
        var aborted = http.RequestAborted;
        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = format.ContentType;
        try
        {
            if (format.Preamble.Length > 0) await http.Response.WriteAsync(format.Preamble, aborted);
            while (await rows.MoveNextAsync(aborted))
            {
                await http.Response.WriteAsync(format.Render(rows.Current.Select(ObservationDto.From)), aborted);
            }
        }
        catch (OperationCanceledException) when (aborted.IsCancellationRequested)
        {
            // The caller hung up; reading stops with the request.
        }
        catch (Exception e)
        {
            http.RequestServices.GetRequiredService<ILogger<ObservationStreamResult>>()
                .LogError(e, "Network diagnostics observation export failed mid-stream; aborting the connection");
            http.Abort();
        }
    }
}
