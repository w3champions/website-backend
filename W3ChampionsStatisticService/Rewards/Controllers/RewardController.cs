#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Common.Repositories;
using W3C.Domain.Common.Services;
using W3C.Domain.Rewards.Repositories;
using W3ChampionsStatisticService.Rewards.DTOs;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Rewards.Controllers;

/// <summary>
/// Controller for CRUD operations on reward definitions
/// </summary>
[ApiController]
[Route("api/rewards")]
public class RewardController(
    IRewardRepository rewardRepo,
    IProductMappingRepository productMappingRepo,
    IEnumerable<IRewardModule> rewardModules,
    IAuditLogService auditLogService,
    ILogger<RewardController> logger) : ControllerBase
{
    private readonly IRewardRepository _rewardRepo = rewardRepo;
    private readonly IProductMappingRepository _productMappingRepo = productMappingRepo;
    private readonly IEnumerable<IRewardModule> _rewardModules = rewardModules;
    private readonly IAuditLogService _auditLogService = auditLogService;
    private readonly ILogger<RewardController> _logger = logger;

    [HttpGet]
    [CheckIfBattleTagIsAdmin]
    public async Task<IActionResult> GetRewards()
    {
        var rewards = await _rewardRepo.GetAll();
        return Ok(rewards);
    }

    [HttpGet("modules")]
    [CheckIfBattleTagIsAdmin]
    public IActionResult GetAvailableModules()
    {
        var modules = _rewardModules.Select(module => new ModuleDefinitionDto
        {
            ModuleId = module.ModuleId,
            ModuleName = module.ModuleName,
            Description = module.Description,
            SupportsParameters = module.SupportsParameters,
            ParameterDefinitions = module.GetParameterDefinitions().ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value)
        }).ToList();

        return Ok(modules);
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
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> CreateReward([FromBody] CreateRewardRequest request, string actingPlayer)
    {
        var reward = new Reward
        {
            DisplayId = request.DisplayId,
            ModuleId = request.ModuleId,
            Parameters = ConvertParametersToObjects(request.Parameters),
            Duration = request.Duration,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _rewardRepo.Create(reward);
        _logger.LogInformation("Created reward {RewardId}: {DisplayId}", reward.Id, reward.DisplayId);

        // Log audit event
        await _auditLogService.LogAdminAction(actingPlayer, "CREATE", "Reward", reward.Id,
            oldValue: null, newValue: reward);

        return Ok(reward);
    }

    [HttpPut("{rewardId}")]
    [CheckIfBattleTagIsAdmin]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> UpdateReward(string rewardId, [FromBody] UpdateRewardRequest request, string actingPlayer)
    {
        var reward = await _rewardRepo.GetById(rewardId);
        if (reward == null)
            return NotFound();

        // Store original values for audit logging
        var originalReward = new Reward
        {
            Id = reward.Id,
            DisplayId = reward.DisplayId,
            ModuleId = reward.ModuleId,
            Parameters = reward.Parameters,
            Duration = reward.Duration,
            IsActive = reward.IsActive,
            CreatedAt = reward.CreatedAt,
            UpdatedAt = reward.UpdatedAt
        };

        // Check if reward is referenced by any product mappings
        var productMappings = await _productMappingRepo.GetByRewardId(rewardId);
        if (productMappings.Any())
        {
            // If trying to change parameters while reward is used in product mappings, reject the change
            if (request.Parameters != null && !AreParametersEqual(reward.Parameters, ConvertParametersToObjects(request.Parameters)))
            {
                var mappingNames = productMappings.Select(m => m.ProductName).ToList();
                var errorMessage = $"Cannot change parameters: This reward is used in {productMappings.Count} product mapping(s):\n• {string.Join("\n• ", mappingNames)}\n\nRemove it from all product mappings before modifying parameters.";

                return BadRequest(new
                {
                    error = errorMessage
                });
            }

            // If trying to change duration while reward is used in product mappings, reject the change
            if (request.Duration != null && !Equals(reward.Duration, request.Duration))
            {
                var mappingNames = productMappings.Select(m => m.ProductName).ToList();
                var errorMessage = $"Cannot change duration: This reward is used in {productMappings.Count} product mapping(s):\n• {string.Join("\n• ", mappingNames)}\n\nRemove it from all product mappings before modifying duration.";

                return BadRequest(new
                {
                    error = errorMessage
                });
            }

            // Note: DisplayId changes are allowed even when reward is used in product mappings
            // This allows admins to update translation keys without breaking existing mappings
        }

        reward.DisplayId = request.DisplayId ?? reward.DisplayId;
        reward.Parameters = request.Parameters != null ? ConvertParametersToObjects(request.Parameters) : reward.Parameters;
        reward.Duration = request.Duration ?? reward.Duration;
        reward.IsActive = request.IsActive ?? reward.IsActive;
        reward.UpdatedAt = DateTime.UtcNow;

        await _rewardRepo.Update(reward);
        _logger.LogInformation("Updated reward {RewardId}", rewardId);

        // Log audit event
        await _auditLogService.LogAdminAction(actingPlayer, "UPDATE", "Reward", reward.Id,
            oldValue: originalReward, newValue: reward);

        return Ok(reward);
    }

    [HttpDelete("{rewardId}")]
    [CheckIfBattleTagIsAdmin]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> DeleteReward(string rewardId, string actingPlayer)
    {
        // Get reward before deletion for audit logging
        var reward = await _rewardRepo.GetById(rewardId);
        if (reward == null)
            return NotFound();

        // Check if reward is referenced by any product mappings
        var productMappings = await _productMappingRepo.GetByRewardId(rewardId);
        if (productMappings.Any())
        {
            var mappingNames = productMappings.Select(m => m.ProductName).ToList();
            var errorMessage = $"Cannot delete reward: It is used in {productMappings.Count} product mapping(s):\n• {string.Join("\n• ", mappingNames)}\n\nRemove it from all product mappings before deleting.";

            return BadRequest(new
            {
                error = errorMessage
            });
        }

        await _rewardRepo.Delete(rewardId);
        _logger.LogInformation("Deleted reward {RewardId}", rewardId);

        // Log audit event
        await _auditLogService.LogAdminAction(actingPlayer, "DELETE", "Reward", rewardId,
            oldValue: reward, newValue: null);

        return NoContent();
    }

    /// <summary>
    /// Helper method to convert Dictionary<string, object> parameters from request to proper objects
    /// </summary>
    private Dictionary<string, object> ConvertParametersToObjects(Dictionary<string, object>? parameters)
    {
        if (parameters == null)
            return new Dictionary<string, object>();

        return parameters.ToDictionary(
            kvp => kvp.Key,
            kvp => ConvertJsonElementToSerializableValue(kvp.Value)
        );
    }

    /// <summary>
    /// Convert JsonElement objects to serializable types for MongoDB
    /// </summary>
    private object ConvertJsonElementToSerializableValue(object? value)
    {
        if (value == null)
            return string.Empty;

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.TryGetInt32(out var intValue) ? intValue : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => element.EnumerateArray().Select(e => ConvertJsonElementToSerializableValue(e)).ToList(),
                JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                    prop => prop.Name,
                    prop => ConvertJsonElementToSerializableValue(prop.Value)),
                JsonValueKind.Null => string.Empty,
                _ => element.ToString() ?? string.Empty
            };
        }

        return value;
    }

    /// <summary>
    /// Helper method to compare parameter dictionaries for equality
    /// </summary>
    private static bool AreParametersEqual(Dictionary<string, object>? dict1, Dictionary<string, object>? dict2)
    {
        if (dict1 == null && dict2 == null) return true;
        if (dict1 == null || dict2 == null) return false;
        if (dict1.Count != dict2.Count) return false;

        foreach (var kvp in dict1)
        {
            if (!dict2.TryGetValue(kvp.Key, out var value2) || !Equals(kvp.Value, value2))
                return false;
        }
        return true;
    }
}
