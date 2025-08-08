using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Abstractions;
using W3ChampionsStatisticService.Rewards.Providers.KoFi;

namespace W3ChampionsStatisticService.Rewards.Controllers;

[ApiController]
[Route("api/rewards/webhooks/kofi")]
public class KoFiWebhookController : ControllerBase
{
    private readonly KoFiProvider _kofiProvider;
    private readonly IRewardService _rewardService;
    private readonly ILogger<KoFiWebhookController> _logger;

    public KoFiWebhookController(
        KoFiProvider kofiProvider,
        IRewardService rewardService,
        ILogger<KoFiWebhookController> logger)
    {
        _kofiProvider = kofiProvider;
        _rewardService = rewardService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> HandleKoFiWebhook([FromForm] string data)
    {
        try
        {
            // Ko-Fi sends data as form-encoded with a 'data' field containing JSON
            if (string.IsNullOrEmpty(data))
            {
                _logger.LogWarning("Ko-Fi webhook received with empty data");
                return BadRequest("Invalid webhook data");
            }

            // Ko-Fi uses verification token inside the JSON payload
            if (!await _kofiProvider.ValidateWebhookSignature(data, null, null))
            {
                _logger.LogWarning("Invalid Ko-Fi webhook verification token");
                return Unauthorized("Invalid verification token");
            }

            // Parse and process event
            var rewardEvent = await _kofiProvider.ParseWebhookEvent(data);
            var assignment = await _rewardService.ProcessRewardEvent(rewardEvent);
            
            _logger.LogInformation("Successfully processed Ko-Fi webhook for user {UserId}", 
                rewardEvent.UserId);
            
            return Ok(new { success = true, assignmentId = assignment?.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Ko-Fi webhook");
            return StatusCode(500, "Internal server error");
        }
    }
}