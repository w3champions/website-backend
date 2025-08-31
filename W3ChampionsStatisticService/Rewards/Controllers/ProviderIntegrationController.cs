using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using W3C.Domain.Common.Repositories;
using W3C.Domain.Common.Services;
using W3C.Domain.Rewards.Repositories;
using W3ChampionsStatisticService.Rewards.Services;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Rewards.Controllers;

/// <summary>
/// Controller for provider integration, OAuth flows, and provider configurations
/// </summary>
[ApiController]
[Route("api/rewards/providers")]
public class ProviderIntegrationController(
    IPatreonAccountLinkRepository patreonLinkRepo,
    PatreonOAuthService patreonOAuthService,
    IAuditLogService auditLogService,
    ILogger<ProviderIntegrationController> logger) : ControllerBase
{
    private readonly IPatreonAccountLinkRepository _patreonLinkRepo = patreonLinkRepo;
    private readonly PatreonOAuthService _patreonOAuthService = patreonOAuthService;
    private readonly IAuditLogService _auditLogService = auditLogService;
    private readonly ILogger<ProviderIntegrationController> _logger = logger;

    [HttpGet]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> GetProviderConfigurations()
    {
        var result = ProviderDefinitions.GetAllProviders().Select(provider => new
        {
            Id = provider.Id,
            ProviderId = provider.Id,
            ProviderName = provider.Name,
            IsActive = provider.IsEnabled,
            Description = provider.Description
        }).ToList();

        return Ok(result);
    }

    [HttpGet("patreon/status")]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> GetPatreonStatus()
    {
        var user = InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext);
        try
        {
            var accountLink = await _patreonLinkRepo.GetByBattleTag(user.BattleTag);

            if (accountLink == null)
            {
                return Ok(new
                {
                    isLinked = false,
                    battleTag = user.BattleTag,
                    patreonUserId = (string?)null,
                    linkedAt = (DateTime?)null,
                    lastSyncAt = (DateTime?)null
                });
            }

            return Ok(new
            {
                isLinked = true,
                battleTag = accountLink.BattleTag,
                patreonUserId = accountLink.PatreonUserId,
                linkedAt = accountLink.LinkedAt,
                lastSyncAt = accountLink.LastSyncAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Patreon status for user {BattleTag}", user.BattleTag);
            return StatusCode(500, new { error = "Failed to retrieve Patreon status" });
        }
    }

    [HttpPost("patreon/oauth/callback")]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> CompletePatreonOAuth([FromBody] CompleteOAuthRequest request)
    {
        var user = InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext);
        if (user == null)
        {
            return Unauthorized("User identification required");
        }

        try
        {
            _logger.LogInformation("Processing Patreon OAuth callback for user {BattleTag}", user.BattleTag);

            var result = await _patreonOAuthService.CompleteOAuthFlow(
                request.Code,
                request.State,
                request.RedirectUri,
                user.BattleTag
            );

            if (!result.Success)
            {
                _logger.LogWarning("Patreon OAuth failed for user {BattleTag}: {Error}", user.BattleTag, result.ErrorMessage);
                return BadRequest(new
                {
                    success = false,
                    error = result.ErrorMessage ?? "OAuth flow failed"
                });
            }

            _logger.LogInformation("Successfully linked Patreon account for user {BattleTag} -> PatreonUserId {PatreonUserId}",
                user.BattleTag, result.PatreonUserId);

            return Ok(new
            {
                success = true,
                battleTag = user.BattleTag,
                patreonUserId = result.PatreonUserId,
                linkedAt = result.LinkedAt,
                message = "Patreon account linked successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Patreon OAuth callback for user {BattleTag}", user.BattleTag);
            return StatusCode(500, new { error = "OAuth process failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Unlink Patreon account
    /// </summary>
    [HttpDelete("patreon/unlink")]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> UnlinkPatreonAccount()
    {
        try
        {
            var user = InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext);
            var result = await _patreonOAuthService.UnlinkAccount(user.BattleTag);

            if (!result)
            {
                return NotFound(new { error = "No Patreon link found to remove" });
            }

            return Ok(new { success = true, message = "Patreon account unlinked successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unlinking Patreon account");
            return StatusCode(500, new { error = "Failed to unlink Patreon account" });
        }
    }
}

/// <summary>
/// DTO for completing OAuth flow
/// </summary>
public class CompleteOAuthRequest
{
    [Required(ErrorMessage = "OAuth code is required")]
    [StringLength(500, MinimumLength = 10, ErrorMessage = "OAuth code must be between 10 and 500 characters")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "State parameter is required")]
    [StringLength(100, MinimumLength = 10, ErrorMessage = "State parameter must be between 10 and 100 characters")]
    public string State { get; set; } = string.Empty;

    [Required(ErrorMessage = "RedirectUri is required")]
    [StringLength(500, MinimumLength = 10, ErrorMessage = "RedirectUri must be between 10 and 500 characters")]
    public string RedirectUri { get; set; } = string.Empty;
}
