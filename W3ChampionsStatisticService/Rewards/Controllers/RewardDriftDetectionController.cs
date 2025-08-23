using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Rewards.Services;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Rewards.Controllers;

[ApiController]
[Route("api/rewards/drift-detection")]
public class RewardDriftDetectionController(
    PatreonDriftDetectionService patreonDriftService,
    ILogger<RewardDriftDetectionController> logger) : ControllerBase
{
    private readonly PatreonDriftDetectionService _patreonDriftService = patreonDriftService;
    private readonly ILogger<RewardDriftDetectionController> _logger = logger;

    [HttpPost("patreon/detect")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> DetectPatreonDrift()
    {
        try
        {
            _logger.LogInformation("Manual Patreon drift detection triggered");

            var result = await _patreonDriftService.DetectDrift();

            return Ok(new
            {
                success = true,
                timestamp = result.Timestamp,
                hasDrift = result.HasDrift,
                summary = new
                {
                    missingMembers = result.MissingMembers.Count,
                    extraAssignments = result.ExtraAssignments.Count,
                    mismatchedTiers = result.MismatchedTiers.Count,
                    totalPatreonMembers = result.TotalPatreonMembers,
                    activePatreonMembers = result.ActivePatreonMembers,
                    totalInternalAssignments = result.TotalInternalAssignments,
                    uniqueInternalUsers = result.UniqueInternalUsers
                },
                details = new
                {
                    missingMembers = result.MissingMembers,
                    extraAssignments = result.ExtraAssignments,
                    mismatchedTiers = result.MismatchedTiers.Select(m => new
                    {
                        userId = m.UserId,
                        patreonMemberId = m.PatreonMemberId,
                        expectedTiers = m.PatreonTiers,
                        actualTiers = m.InternalTiers,
                        issue = m.Reason
                    })
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during manual Patreon drift detection");
            return StatusCode(500, new { success = false, error = "An error occurred during drift detection" });
        }
    }

    [HttpGet("status")]
    [CheckIfBattleTagIsAdmin]
    public IActionResult GetDriftDetectionStatus()
    {
        // This could be enhanced to return last run time, next scheduled run, etc.
        return Ok(new
        {
            enabled = true, // This should come from configuration
            providers = new[]
            {
                new { provider = "patreon", available = true },
                new { provider = "kofi", available = false } // Not implemented yet
            }
        });
    }
}
