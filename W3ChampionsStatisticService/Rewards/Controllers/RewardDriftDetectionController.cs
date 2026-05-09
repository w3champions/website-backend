using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using W3ChampionsStatisticService.Rewards.BackgroundServices;
using W3ChampionsStatisticService.Rewards.Services;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3C.Domain.Common.Services;

namespace W3ChampionsStatisticService.Rewards.Controllers;

[ApiController]
[Route("api/rewards/drift-detection")]
public class RewardDriftDetectionController(
    PatreonDriftDetectionService patreonDriftService,
    IAuditLogService auditLogService,
    ILogger<RewardDriftDetectionController> logger,
    RewardDriftDetectionBackgroundService backgroundService) : ControllerBase
{
    private readonly PatreonDriftDetectionService _patreonDriftService = patreonDriftService;
    private readonly IAuditLogService _auditLogService = auditLogService;
    private readonly ILogger<RewardDriftDetectionController> _logger = logger;
    private readonly RewardDriftDetectionBackgroundService _backgroundService = backgroundService;

    [HttpPost("patreon/detect")]
    [CheckIfBattleTagIsAdmin]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> DetectPatreonDrift(string actingPlayer)
    {
        try
        {
            _logger.LogInformation("Manual Patreon drift detection triggered by {BattleTag}", actingPlayer);

            var result = await _patreonDriftService.DetectDrift();

            // Log audit event
            var totalAffected = result.MissingMembers.Count + result.ExtraAssignments.Count + result.MismatchedTiers.Count;
            await _auditLogService.LogSystemAction(actingPlayer, "DRIFT_DETECTION", "DETECT", "patreon", totalAffected,
                new Dictionary<string, object>
                {
                    ["has_drift"] = result.HasDrift,
                    ["missing_members"] = result.MissingMembers.Count,
                    ["extra_assignments"] = result.ExtraAssignments.Count,
                    ["mismatched_tiers"] = result.MismatchedTiers.Count
                });

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

    [HttpPost("patreon/sync")]
    [CheckIfBattleTagIsAdmin]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> SyncPatreonDrift([FromBody] DriftDetectionSyncRequest request, string actingPlayer, [FromQuery] bool dryRun = true)
    {
        try
        {
            _logger.LogInformation("Patreon drift sync triggered by {BattleTag}, DryRun: {DryRun}", actingPlayer, dryRun);

            var result = await _patreonDriftService.SyncDrift(request.DriftResult, dryRun);

            // Log audit event
            await _auditLogService.LogSystemAction(actingPlayer, "DRIFT_SYNC", dryRun ? "PREVIEW" : "EXECUTE", "patreon",
                result.MembersAdded + result.TiersUpdated,
                new Dictionary<string, object>
                {
                    ["dry_run"] = dryRun,
                    ["members_added"] = result.MembersAdded,
                    ["tiers_updated"] = result.TiersUpdated,
                    ["success"] = result.Success,
                    ["errors_count"] = result.Errors.Count
                });

            return Ok(new
            {
                success = result.Success,
                wasDryRun = dryRun,
                membersAdded = result.MembersAdded,
                tiersUpdated = result.TiersUpdated,
                assignmentsRevoked = result.AssignmentsRevoked,
                processedAssociations = result.ProcessedAssociations,
                errors = result.Errors,
                startedAt = DateTime.UtcNow.ToString("O"),
                completedAt = DateTime.UtcNow.ToString("O")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Patreon drift sync");
            return StatusCode(500, new { success = false, error = "An error occurred during drift sync" });
        }
    }

    [HttpGet("status")]
    [CheckIfBattleTagIsAdmin]
    public IActionResult GetDriftDetectionStatus()
    {
        static bool ParseBool(string envVar, bool defaultValue)
        {
            var raw = Environment.GetEnvironmentVariable(envVar);
            if (string.IsNullOrEmpty(raw)) return defaultValue;
            return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
        }

        var detectionEnabled = ParseBool("REWARDS_DRIFT_DETECTION_ENABLED", false);
        var autoSyncEnabled = ParseBool("REWARDS_DRIFT_AUTO_SYNC_ENABLED", false);
        var dryRun = ParseBool("REWARDS_DRIFT_SYNC_DRY_RUN", true);
        var ignoredTierIds = Environment.GetEnvironmentVariable("REWARDS_PATREON_IGNORED_TIER_IDS") ?? "";

        return Ok(new
        {
            detectionEnabled,
            autoSyncEnabled,
            dryRun,
            ignoredTierIds,
            lastRun = new
            {
                startedAtUtc = _backgroundService.LastRunStartedAtUtc?.ToString("O"),
                completedAtUtc = _backgroundService.LastRunCompletedAtUtc?.ToString("O"),
                succeeded = _backgroundService.LastRunSucceeded,
                errorMessage = _backgroundService.LastRunErrorMessage,
                membersAdded = _backgroundService.LastRunMembersAdded,
                assignmentsRevoked = _backgroundService.LastRunAssignmentsRevoked,
                tiersUpdated = _backgroundService.LastRunTiersUpdated
            },
            providers = new[]
            {
                new { provider = "patreon", available = true },
                new { provider = "kofi", available = false }
            }
        });
    }
}

public class DriftDetectionSyncRequest
{
    public required DriftDetectionResult DriftResult { get; set; }
}
