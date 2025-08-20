using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
using W3ChampionsStatisticService.Rewards.Services;
using W3ChampionsStatisticService.Rewards;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Rewards.Controllers;

[ApiController]
[Route("api/rewards")]
public class RewardManagementController(
    IRewardRepository rewardRepo,
    IRewardAssignmentRepository assignmentRepo,
    IProviderConfigurationRepository configRepo,
    PatreonOAuthService patreonOAuthService,
    IW3CAuthenticationService authService,
    ILogger<RewardManagementController> logger) : ControllerBase
{
    private readonly IRewardRepository _rewardRepo = rewardRepo;
    private readonly IRewardAssignmentRepository _assignmentRepo = assignmentRepo;
    private readonly IProviderConfigurationRepository _configRepo = configRepo;
    private readonly PatreonOAuthService _patreonOAuthService = patreonOAuthService;
    private readonly IW3CAuthenticationService _authService = authService;
    private readonly ILogger<RewardManagementController> _logger = logger;

    [HttpGet]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> GetRewards()
    {
        var rewards = await _rewardRepo.GetAll();
        return Ok(rewards);
    }

    [HttpGet("{rewardId}")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> GetReward(string rewardId)
    {
        var reward = await _rewardRepo.GetById(rewardId);
        if (reward == null)
            return NotFound();
        return Ok(reward);
    }

    [HttpPost]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> CreateReward([FromBody] CreateRewardRequest request)
    {
        var reward = new Reward
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            ModuleId = request.ModuleId,
            Parameters = ConvertParametersToObjects(request.Parameters),
            Duration = request.Duration,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _rewardRepo.Create(reward);
        _logger.LogInformation("Created reward {RewardId}: {Name}", reward.Id, reward.Name);

        return Ok(reward);
    }

    [HttpPut("{rewardId}")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> UpdateReward(string rewardId, [FromBody] UpdateRewardRequest request)
    {
        var reward = await _rewardRepo.GetById(rewardId);
        if (reward == null)
            return NotFound();

        reward.Name = request.Name ?? reward.Name;
        reward.Description = request.Description ?? reward.Description;
        reward.Type = request.Type ?? reward.Type;
        reward.Parameters = request.Parameters != null ? ConvertParametersToObjects(request.Parameters) : reward.Parameters;
        reward.Duration = request.Duration ?? reward.Duration;
        reward.IsActive = request.IsActive ?? reward.IsActive;
        reward.UpdatedAt = DateTime.UtcNow;

        await _rewardRepo.Update(reward);
        _logger.LogInformation("Updated reward {RewardId}", rewardId);

        return Ok(reward);
    }

    [HttpDelete("{rewardId}")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> DeleteReward(string rewardId)
    {
        await _rewardRepo.Delete(rewardId);
        _logger.LogInformation("Deleted reward {RewardId}", rewardId);
        return NoContent();
    }

    [HttpGet("assignments/{userId}")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> GetUserRewards(string userId)
    {
        var assignments = await _assignmentRepo.GetByUserId(userId);
        return Ok(assignments);
    }

    [HttpGet("providers")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> GetProviderConfigurations()
    {
        var dbConfigs = await _configRepo.GetAll();
        var dbConfigsByProvider = dbConfigs.ToDictionary(c => c.ProviderId, c => c);

        var result = ProviderDefinitions.GetAllProviders().Select(provider => new
        {
            Id = provider.Id,
            ProviderId = provider.Id,
            ProviderName = provider.Name,
            IsActive = provider.IsEnabled,
            Description = provider.Description,
            ProductMappings = dbConfigsByProvider.TryGetValue(provider.Id, out var config) ? 
                            config.ProductMappings : new List<ProductMapping>(),
            CreatedAt = dbConfigsByProvider.TryGetValue(provider.Id, out var config2) ? 
                       config2.CreatedAt : DateTime.UtcNow,
            UpdatedAt = dbConfigsByProvider.TryGetValue(provider.Id, out var config3) ? 
                       config3.UpdatedAt : null
        }).ToList();

        return Ok(result);
    }

    [HttpPost("providers/{providerId}/mappings")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> AddProductMapping(string providerId, [FromBody] ProductMapping mapping)
    {
        // Validate that the provider is supported
        if (!ProviderDefinitions.IsProviderSupported(providerId))
        {
            return BadRequest(new { error = $"Provider '{providerId}' is not supported" });
        }

        var config = await _configRepo.GetByProviderId(providerId);
        if (config == null)
        {
            config = new ProviderConfiguration
            {
                Id = Guid.NewGuid().ToString(),
                ProviderId = providerId,
                ProductMappings = new List<ProductMapping> { mapping },
                CreatedAt = DateTime.UtcNow
            };
            await _configRepo.Create(config);
        }
        else
        {
            config.ProductMappings.Add(mapping);
            config.UpdatedAt = DateTime.UtcNow;
            await _configRepo.Update(config);
        }

        _logger.LogInformation("Added product mapping for {ProviderId}: {ProductId} -> {RewardId}",
            providerId, mapping.ProviderProductId, mapping.RewardId);

        // Return the combined provider info with mappings
        var provider = ProviderDefinitions.GetProvider(providerId);
        var result = new
        {
            Id = provider.Id,
            ProviderId = provider.Id,
            ProviderName = provider.Name,
            IsActive = provider.IsEnabled,
            Description = provider.Description,
            ProductMappings = config.ProductMappings,
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt
        };

        return Ok(result);
    }

    [HttpDelete("providers/{providerId}/mappings/{productId}")]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> RemoveProductMapping(string providerId, string productId)
    {
        var config = await _configRepo.GetByProviderId(providerId);
        if (config == null)
            return NotFound();

        config.ProductMappings.RemoveAll(m => m.ProviderProductId == productId);
        config.UpdatedAt = DateTime.UtcNow;
        await _configRepo.Update(config);

        _logger.LogInformation("Removed product mapping for {ProviderId}: {ProductId}", providerId, productId);

        return NoContent();
    }

    // Patreon OAuth endpoints
    
    /// <summary>
    /// Get Patreon link status for authenticated user
    /// </summary>
    [HttpGet("patreon/status")]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> GetPatreonStatus()
    {
        try
        {
            var user = InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext);
            var status = await _patreonOAuthService.GetLinkStatus(user.BattleTag);
            
            return Ok(new PatreonStatusResponse
            {
                IsLinked = status.IsLinked,
                PatreonUserId = status.PatreonUserId,
                LinkedAt = status.LinkedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Patreon status");
            return StatusCode(500, new { error = "Failed to get Patreon status" });
        }
    }

    /// <summary>
    /// Complete Patreon OAuth flow
    /// </summary>
    [HttpPost("patreon/oauth/callback")]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> CompletePatreonOAuth([FromBody] CompleteOAuthRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.Code))
                return BadRequest(new { error = "Authorization code is required" });

            if (string.IsNullOrEmpty(request.State))
                return BadRequest(new { error = "State parameter is required" });

            var user = InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext);
            if (string.IsNullOrWhiteSpace(request.RedirectUri))
                return BadRequest(new { error = "RedirectUri is required and must exactly match the URI used in the Patreon authorize step" });

            var redirectUri = request.RedirectUri;
            // Normalize if client passed a URL-encoded redirect URI (to avoid double-encoding when posting the token request)
            if (redirectUri.Contains("%"))
            {
                try
                {
                    var decoded = System.Uri.UnescapeDataString(redirectUri);
                    if (System.Uri.TryCreate(decoded, System.UriKind.Absolute, out _))
                    {
                        redirectUri = decoded;
                    }
                }
                catch { /* ignore decoding issues and use the original value */ }
            }
            
            var result = await _patreonOAuthService.CompleteOAuthFlow(
                request.Code, 
                request.State, 
                redirectUri, 
                user.BattleTag);

            if (!result.Success)
            {
                return BadRequest(new { error = result.ErrorMessage });
            }

            return Ok(new CompleteOAuthResponse
            {
                Success = true,
                IsLinked = true,
                PatreonUserId = result.PatreonUserId,
                LinkedAt = result.LinkedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing Patreon OAuth");
            return StatusCode(500, new { error = "Failed to complete OAuth process" });
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

    // User-facing rewards endpoints
    
    /// <summary>
    /// Get current user's reward assignments
    /// </summary>
    [HttpGet("assignments")]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> GetUserAssignments()
    {
        try
        {
            var user = InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext);
            var assignments = await _assignmentRepo.GetByUserId(user.BattleTag);
            
            return Ok(assignments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user assignments");
            return StatusCode(500, new { error = "Failed to get user assignments" });
        }
    }

    private static Dictionary<string, object> ConvertParametersToObjects(Dictionary<string, object> parameters)
    {
        if (parameters == null)
            return new Dictionary<string, object>();

        var result = new Dictionary<string, object>();
        
        foreach (var kvp in parameters)
        {
            if (kvp.Value is System.Text.Json.JsonElement jsonElement)
            {
                result[kvp.Key] = ConvertJsonElementToObject(jsonElement);
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }

    private static object ConvertJsonElementToObject(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => element.GetString(),
            System.Text.Json.JsonValueKind.Number => element.TryGetInt32(out var intValue) ? intValue : element.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null,
            System.Text.Json.JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElementToObject).ToArray(),
            System.Text.Json.JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElementToObject(p.Value)),
            _ => element.ToString()
        };
    }

}

public class CreateRewardRequest
{
    public string Name { get; set; }
    public string Description { get; set; }
    public RewardType Type { get; set; }
    public string ModuleId { get; set; }
    public Dictionary<string, object> Parameters { get; set; }
    public RewardDuration Duration { get; set; }
}

public class UpdateRewardRequest
{
    public string Name { get; set; }
    public string Description { get; set; }
    public RewardType? Type { get; set; }
    public Dictionary<string, object> Parameters { get; set; }
    public RewardDuration Duration { get; set; }
    public bool? IsActive { get; set; }
}

public class CompleteOAuthRequest
{
    public string Code { get; set; }
    public string State { get; set; }
    public string RedirectUri { get; set; }
}

public class CompleteOAuthResponse
{
    public bool Success { get; set; }
    public bool IsLinked { get; set; }
    public string PatreonUserId { get; set; }
    public DateTime? LinkedAt { get; set; }
    public string ErrorMessage { get; set; }
}

public class PatreonStatusResponse
{
    public bool IsLinked { get; set; }
    public string PatreonUserId { get; set; }
    public DateTime? LinkedAt { get; set; }
}