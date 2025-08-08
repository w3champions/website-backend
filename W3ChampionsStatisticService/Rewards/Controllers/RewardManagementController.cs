using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Rewards.Controllers;

[ApiController]
[Route("api/rewards")]
public class RewardManagementController : ControllerBase
{
    private readonly IRewardRepository _rewardRepo;
    private readonly IRewardAssignmentRepository _assignmentRepo;
    private readonly IProviderConfigurationRepository _configRepo;
    private readonly ILogger<RewardManagementController> _logger;

    public RewardManagementController(
        IRewardRepository rewardRepo,
        IRewardAssignmentRepository assignmentRepo,
        IProviderConfigurationRepository configRepo,
        ILogger<RewardManagementController> logger)
    {
        _rewardRepo = rewardRepo;
        _assignmentRepo = assignmentRepo;
        _configRepo = configRepo;
        _logger = logger;
    }

    [HttpGet]
    [CheckIfBattleTagIsAdminFilter]
    public async Task<IActionResult> GetRewards()
    {
        var rewards = await _rewardRepo.GetAll();
        return Ok(rewards);
    }

    [HttpGet("{rewardId}")]
    [CheckIfBattleTagIsAdminFilter]
    public async Task<IActionResult> GetReward(string rewardId)
    {
        var reward = await _rewardRepo.GetById(rewardId);
        if (reward == null)
            return NotFound();
        return Ok(reward);
    }

    [HttpPost]
    [CheckIfBattleTagIsAdminFilter]
    public async Task<IActionResult> CreateReward([FromBody] CreateRewardRequest request)
    {
        var reward = new Reward
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            ModuleId = request.ModuleId,
            Parameters = request.Parameters ?? new Dictionary<string, object>(),
            Duration = request.Duration,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _rewardRepo.Create(reward);
        _logger.LogInformation("Created reward {RewardId}: {Name}", reward.Id, reward.Name);
        
        return Ok(reward);
    }

    [HttpPut("{rewardId}")]
    [CheckIfBattleTagIsAdminFilter]
    public async Task<IActionResult> UpdateReward(string rewardId, [FromBody] UpdateRewardRequest request)
    {
        var reward = await _rewardRepo.GetById(rewardId);
        if (reward == null)
            return NotFound();

        reward.Name = request.Name ?? reward.Name;
        reward.Description = request.Description ?? reward.Description;
        reward.Parameters = request.Parameters ?? reward.Parameters;
        reward.Duration = request.Duration ?? reward.Duration;
        reward.IsActive = request.IsActive ?? reward.IsActive;
        reward.UpdatedAt = DateTime.UtcNow;

        await _rewardRepo.Update(reward);
        _logger.LogInformation("Updated reward {RewardId}", rewardId);
        
        return Ok(reward);
    }

    [HttpDelete("{rewardId}")]
    [CheckIfBattleTagIsAdminFilter]
    public async Task<IActionResult> DeleteReward(string rewardId)
    {
        await _rewardRepo.Delete(rewardId);
        _logger.LogInformation("Deleted reward {RewardId}", rewardId);
        return NoContent();
    }

    [HttpGet("assignments/{userId}")]
    [CheckIfBattleTagIsAdminFilter]
    public async Task<IActionResult> GetUserRewards(string userId)
    {
        var assignments = await _assignmentRepo.GetByUserId(userId);
        return Ok(assignments);
    }

    [HttpGet("providers")]
    [CheckIfBattleTagIsAdminFilter]
    public async Task<IActionResult> GetProviderConfigurations()
    {
        var configs = await _configRepo.GetAll();
        return Ok(configs);
    }

    [HttpPost("providers/{providerId}/mappings")]
    [CheckIfBattleTagIsAdminFilter]
    public async Task<IActionResult> AddProductMapping(string providerId, [FromBody] ProductMapping mapping)
    {
        var config = await _configRepo.GetByProviderId(providerId);
        if (config == null)
        {
            config = new ProviderConfiguration
            {
                Id = Guid.NewGuid().ToString(),
                ProviderId = providerId,
                ProviderName = providerId,
                IsActive = true,
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
        
        return Ok(config);
    }

    [HttpDelete("providers/{providerId}/mappings/{productId}")]
    [CheckIfBattleTagIsAdminFilter]
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
    public Dictionary<string, object> Parameters { get; set; }
    public RewardDuration Duration { get; set; }
    public bool? IsActive { get; set; }
}