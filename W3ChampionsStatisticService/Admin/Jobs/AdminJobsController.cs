using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.Common.Services;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Admin.Jobs;

/// <summary>
/// Manual trigger for operational jobs, so one-offs like the MMR timeline backfill do
/// not need shell access to the environment. See docs/admin-job-runner.md.
/// </summary>
[ApiController]
[Route("api/admin/jobs")]
[Trace]
public class AdminJobsController(
    IEnumerable<IAdminJob> jobs,
    IAdminJobRepository jobRepository,
    AdminJobRunner runner,
    IAuditLogService auditLogService) : ControllerBase
{
    private const string AuditCategory = "JOB";

    private readonly List<IAdminJob> _jobs = jobs.OrderBy(j => j.Name).ToList();

    [HttpGet]
    [BearerHasPermissionFilter(Permission = EPermission.Jobs)]
    public async Task<IActionResult> GetJobs()
    {
        var states = (await jobRepository.LoadAll()).ToDictionary(j => j.Id);

        return Ok(_jobs
            .Select(job => AdminJobDto.From(job, states.GetValueOrDefault(job.Key)))
            .ToList());
    }

    [HttpPost("{key}/run")]
    [BearerHasPermissionFilter(Permission = EPermission.Jobs)]
    public async Task<IActionResult> RunJob(
        string key,
        string battleTag,
        [FromQuery] bool force = false,
        [FromQuery] bool reset = false)
    {
        var job = _jobs.FirstOrDefault(j => j.Key == key);
        if (job == null)
        {
            return NotFound(new { error = "unknown_job", key });
        }

        if (!CallerHasJobPermission(job))
        {
            return StatusCode(403, new { error = "permission_missing", permission = job.RequiredPermission.ToString() });
        }

        var result = await runner.TryStart(key, battleTag, force, reset);
        if (result == AdminJobStartResult.NotFound)
        {
            return NotFound(new { error = "unknown_job", key });
        }

        if (result == AdminJobStartResult.NotStartable)
        {
            // Either it is already running, or it has completed and the caller did not
            // ask to run it again.
            return Conflict(new { error = "job_not_startable", key });
        }

        await auditLogService.LogAction(
            battleTag,
            AuditCategory,
            reset ? "RUN_RESET" : force ? "RUN_FORCE" : "RUN",
            nameof(AdminJob),
            key);

        return Ok(await LoadDto(job));
    }

    [HttpPost("{key}/cancel")]
    [BearerHasPermissionFilter(Permission = EPermission.Jobs)]
    public async Task<IActionResult> CancelJob(string key, string battleTag)
    {
        var job = _jobs.FirstOrDefault(j => j.Key == key);
        if (job == null)
        {
            return NotFound(new { error = "unknown_job", key });
        }

        if (!CallerHasJobPermission(job))
        {
            return StatusCode(403, new { error = "permission_missing", permission = job.RequiredPermission.ToString() });
        }

        if (!runner.Cancel(key))
        {
            return Conflict(new { error = "job_not_running", key });
        }

        await auditLogService.LogAction(battleTag, AuditCategory, "CANCEL", nameof(AdminJob), key);

        // Cancellation is cooperative, so the job is still winding down here. The client
        // sees the terminal status on its next poll.
        return Ok(await LoadDto(job));
    }

    private async Task<AdminJobDto> LoadDto(IAdminJob job) =>
        AdminJobDto.From(job, await jobRepository.Load(job.Key));

    /// <summary>
    /// A job may require more than <see cref="EPermission.Jobs"/>, which is all the
    /// filter attribute can express - it is static, and the job is only known per
    /// request. Re-reads the caller's token rather than trusting anything from the
    /// route.
    /// </summary>
    private bool CallerHasJobPermission(IAdminJob job)
    {
        if (job.RequiredPermission == EPermission.Jobs)
        {
            return true;
        }

        var token = BearerHasPermissionFilter.GetToken(Request.Headers[HeaderNames.Authorization]);
        var user = new W3CAuthenticationService().GetUserByToken(token, true);
        return user.Permissions.Contains(job.RequiredPermission);
    }
}
