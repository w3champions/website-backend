using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using W3C.Contracts.Admin.Permission;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Tracing;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Admin.ReplayExport;

/// <summary>
/// Admin bulk download of replays. Mongo (Matchup) selects the games; replay-service builds each .w3g from
/// the FLO archive in S3, the same way as the public single-replay download. So this service needs no S3
/// credentials. The public, paid download is ReplayStoreController.
/// </summary>
[ApiController]
[Route("api/admin/replay-export")]
[Trace]
public class ReplayExportController(
    ReplayExportRepository repository,
    ReplayExportTicketStore tickets,
    ReplayServiceClient replayServiceClient,
    ILogger<ReplayExportController> logger) : ReplayExportControllerBase(repository, tickets, replayServiceClient, logger)
{
    private static readonly SemaphoreSlim AdminExports = new(MaxRunningExports, MaxRunningExports);

    protected override SemaphoreSlim RunningExports => AdminExports;

    protected override string TicketScope => "admin";

    /// <summary>The past downloads of the acting user (<paramref name="battleTag"/>). Each user has their own history.</summary>
    [HttpGet("exports")]
    [BearerHasPermissionFilter(Permission = EPermission.Replays)]
    public async Task<IActionResult> GetExports(string battleTag)
    {
        return Ok(await Repository.LoadExports(battleTag));
    }

    /// <summary>For admins: the downloads of all users, or of one <paramref name="user"/>.</summary>
    [HttpGet("all-exports")]
    [BearerHasPermissionFilter(Permission = EPermission.Replays)]
    public async Task<IActionResult> GetAllExports([FromQuery] string user)
    {
        return Ok(await Repository.LoadAllExports(user));
    }

    [HttpGet("options")]
    [BearerHasPermissionFilter(Permission = EPermission.Replays)]
    public async Task<IActionResult> GetOptions([FromQuery] GameMode gameMode = GameMode.GM_1v1)
    {
        return Ok(await Repository.LoadOptions(gameMode, new ReplayExportPrices { Free = true }));
    }

    [HttpPost("preview")]
    [BearerHasPermissionFilter(Permission = EPermission.Replays)]
    public Task<IActionResult> Preview([FromBody] ReplayExportFilter filter, string battleTag) => Search(filter, battleTag);

    /// <summary>
    /// The games of one table page, in the order of the ids. A search gives only the ids of the listed games
    /// (see <see cref="ReplayExportPreview"/>), so the page gets each table page when it shows it.
    /// </summary>
    [HttpPost("matches")]
    [BearerHasPermissionFilter(Permission = EPermission.Replays)]
    public Task<IActionResult> LoadMatches([FromBody] ReplayExportMatchesRequest request) => TablePage(request);

    /// <summary>
    /// Step 1 of a download: an authenticated request turns the selected games into a single-use ticket.
    /// <paramref name="battleTag"/> is the acting admin, set by <see cref="BearerHasPermissionFilter"/>.
    /// </summary>
    [HttpPost("tickets")]
    [BearerHasPermissionFilter(Permission = EPermission.Replays)]
    public IActionResult CreateTicket([FromBody] ReplayExportTicketRequest request, string battleTag)
    {
        var max = ReplayExportRepository.MaxQuantity;
        var error = ParseMatchIds(request?.MatchIds, max, $"At most {max:N0} replays per download.", out var ids);
        if (error != null) return BadRequest(new { Error = error });
        if (ids.Count == 0) return BadRequest(new { Error = "Select at least one game." });

        var busy = DownloadBusy(battleTag);
        if (busy != null) return busy;

        var (ticket, expiresAt) = Tickets.Create(new ReplayExportTicket
        {
            MatchIds = ids,
            RequestedBy = battleTag,
            Label = CleanText(request.Label, 100),
            Description = CleanText(request.Description, 500),
            // Only for the manifest and the history. A filter that a search would not accept is not kept.
            Filter = request.Filter != null && Validate(request.Filter) == null ? request.Filter : null,
            Scope = TicketScope,
        });
        return Ok(new ReplayExportTicketResponse
        {
            Ticket = ticket,
            Count = ids.Count,
            ExpiresAt = expiresAt,
            Price = ReplayExportPricing.Price(ids.Count),
            // Admins download for free. The replay store takes the payment before the download.
            Charged = false,
        });
    }

    /// <summary>A repeat download of a download in the history. <paramref name="battleTag"/> is the acting admin.</summary>
    [HttpPost("exports/{exportId}/tickets")]
    [BearerHasPermissionFilter(Permission = EPermission.Replays)]
    public async Task<IActionResult> CreateRepeatTicket([FromRoute] string exportId, string battleTag)
    {
        return RepeatTicket(await Repository.LoadExport(exportId, battleTag), battleTag);
    }

    /// <summary>For admins: a repeat download of any user's download. It shows in that download's history.</summary>
    [HttpPost("all-exports/{exportId}/tickets")]
    [BearerHasPermissionFilter(Permission = EPermission.Replays)]
    public async Task<IActionResult> CreateAdminRepeatTicket([FromRoute] string exportId, string battleTag)
    {
        return RepeatTicket(await Repository.LoadAnyExport(exportId), battleTag);
    }

    /// <summary>Step 2 of a download: the browser streams the ZIP. The ticket from step 1 authorizes it.</summary>
    [HttpGet("download/{ticket}")]
    public Task<IActionResult> Download([FromRoute] string ticket) => StreamDownload(ticket);
}
