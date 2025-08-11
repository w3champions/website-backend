using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Abstractions;
using W3ChampionsStatisticService.Rewards.Providers.Patreon;

namespace W3ChampionsStatisticService.Rewards.Controllers;

[ApiController]
[Route("api/rewards/webhooks/patreon")]
public class PatreonWebhookController(
    PatreonProvider patreonProvider,
    IRewardService rewardService,
    ILogger<PatreonWebhookController> logger) : ControllerBase
{
    private readonly PatreonProvider _patreonProvider = patreonProvider;
    private readonly IRewardService _rewardService = rewardService;
    private readonly ILogger<PatreonWebhookController> _logger = logger;

    [HttpPost]
    public async Task<IActionResult> HandlePatreonWebhook()
    {
        try
        {
            // Read raw body for signature validation
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            var payload = await reader.ReadToEndAsync();
            
            // Patreon sends signature in X-Patreon-Signature header
            var signature = Request.Headers["X-Patreon-Signature"].FirstOrDefault();
            
            if (string.IsNullOrEmpty(signature))
            {
                _logger.LogWarning("Patreon webhook received without signature");
                return Unauthorized("Missing signature");
            }

            // Validate webhook signature
            if (!await _patreonProvider.ValidateWebhookSignature(payload, signature, null))
            {
                _logger.LogWarning("Invalid Patreon webhook signature");
                return Unauthorized("Invalid signature");
            }

            // Extract headers for event type determination
            var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.FirstOrDefault());
            
            // Parse and process event
            var rewardEvent = await _patreonProvider.ParseWebhookEvent(payload, headers);
            var assignment = await _rewardService.ProcessRewardEvent(rewardEvent);
            
            _logger.LogInformation("Successfully processed Patreon webhook for user {UserId} with {TierCount} entitled tiers", 
                rewardEvent.UserId, rewardEvent.EntitledTierIds.Count);
            
            return Ok(new { success = true, assignmentId = assignment?.Id, entitledTiers = rewardEvent.EntitledTierIds });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Patreon webhook");
            return StatusCode(500, "Internal server error");
        }
    }
}