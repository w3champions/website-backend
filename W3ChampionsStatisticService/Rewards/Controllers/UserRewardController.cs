using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Repositories;
using W3ChampionsStatisticService.Rewards.DTOs;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3C.Domain.Rewards.Abstractions;

namespace W3ChampionsStatisticService.Rewards.Controllers;

/// <summary>
/// Controller for user-facing reward operations
/// </summary>
[ApiController]
[Route("api/rewards")]
public class UserRewardController(
    IRewardAssignmentRepository assignmentRepo,
    IRewardRepository rewardRepo,
    IEnumerable<IRewardModule> rewardModules,
    ILogger<UserRewardController> logger) : ControllerBase
{
    private readonly IRewardAssignmentRepository _assignmentRepo = assignmentRepo;
    private readonly IRewardRepository _rewardRepo = rewardRepo;
    private readonly IEnumerable<IRewardModule> _rewardModules = rewardModules;
    private readonly ILogger<UserRewardController> _logger = logger;

    /// <summary>
    /// Get current user's active rewards
    /// </summary>
    [HttpGet("assignments")]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> GetUserAssignments()
    {
        try
        {
            var user = InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext);
            var assignments = await _assignmentRepo.GetByUserId(user.BattleTag);

            // Backend filtering: Only process active assignments
            var activeAssignments = assignments.Where(a => a.IsActive()).ToList();

            // Fetch reward details and transform to DTOs
            var userRewards = new List<UserRewardDto>();
            foreach (var assignment in activeAssignments)
            {
                var reward = await _rewardRepo.GetById(assignment.RewardId);
                if (reward != null && reward.IsActive)
                {
                    var module = _rewardModules.FirstOrDefault(m => m.ModuleId == reward.ModuleId);
                    userRewards.Add(new UserRewardDto
                    {
                        Id = reward.Id,
                        DisplayId = reward.DisplayId,
                        ModuleId = reward.ModuleId,
                        ModuleName = module?.ModuleName ?? reward.ModuleId,
                        AssignedAt = assignment.AssignedAt,
                        ExpiresAt = assignment.ExpiresAt
                    });
                }
            }

            return Ok(userRewards);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user assignments");
            return StatusCode(500, new { error = "Failed to get user assignments" });
        }
    }
}
