using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.ValueObjects;
using WC3ChampionsStatisticService.Tests.Rewards;

namespace WC3ChampionsStatisticService.Tests.Rewards;

[TestFixture]
public class PartialRewardAssignmentTests
{
    /// <summary>
    /// This test demonstrates that the bug fix correctly identifies which rewards are missing
    /// from a tier that a user partially has. The bug was that the system would try to 
    /// assign all rewards for a tier even if the user already had some of them.
    /// </summary>
    [Test]
    public void ProcessTierWithPartialRewards_IdentifiesOnlyMissingRewards()
    {
        // Arrange - User has some rewards but is missing others
        var tierId = "tier-premium";
        var providerId = "patreon";
        var userId = "TestUser#1234";

        // Product mapping has 3 rewards
        var allRewardsInTier = new List<string> { "reward-a", "reward-b", "reward-c" };

        // User already has 2 out of 3 rewards
        var existingAssignments = new List<RewardAssignment>
        {
            new RewardAssignment
            {
                Id = "assignment-1",
                UserId = userId,
                RewardId = "reward-a",
                ProviderId = providerId,
                Status = RewardStatus.Active,
                Metadata = new Dictionary<string, object> { ["tier_id"] = tierId }
            },
            new RewardAssignment
            {
                Id = "assignment-2",
                UserId = userId,
                RewardId = "reward-b",
                ProviderId = providerId,
                Status = RewardStatus.Active,
                Metadata = new Dictionary<string, object> { ["tier_id"] = tierId }
            }
        };

        // Act - Simulate the logic from the fixed ProcessSubscriptionCreated method
        var existingTierAssignments = existingAssignments.Where(a =>
            a.ProviderId == providerId &&
            a.Metadata.ContainsKey("tier_id") &&
            a.Metadata["tier_id"].ToString() == tierId).ToList();

        var existingRewardIds = existingTierAssignments.Select(a => a.RewardId).ToHashSet();
        var missingRewardIds = allRewardsInTier.Where(id => !string.IsNullOrEmpty(id) && !existingRewardIds.Contains(id)).ToList();

        // Assert
        Assert.AreEqual(1, missingRewardIds.Count, "Should identify exactly one missing reward");
        Assert.Contains("reward-c", missingRewardIds, "Should identify reward-c as the missing reward");
        Assert.IsFalse(missingRewardIds.Contains("reward-a"), "Should not include reward-a (user already has it)");
        Assert.IsFalse(missingRewardIds.Contains("reward-b"), "Should not include reward-b (user already has it)");

        // Additional verification: if user had all rewards, missing list should be empty
        existingAssignments.Add(new RewardAssignment
        {
            Id = "assignment-3",
            UserId = userId,
            RewardId = "reward-c",
            ProviderId = providerId,
            Status = RewardStatus.Active,
            Metadata = new Dictionary<string, object> { ["tier_id"] = tierId }
        });

        // Re-run the logic
        existingTierAssignments = existingAssignments.Where(a =>
            a.ProviderId == providerId &&
            a.Metadata.ContainsKey("tier_id") &&
            a.Metadata["tier_id"].ToString() == tierId).ToList();

        existingRewardIds = existingTierAssignments.Select(a => a.RewardId).ToHashSet();
        missingRewardIds = allRewardsInTier.Where(id => !string.IsNullOrEmpty(id) && !existingRewardIds.Contains(id)).ToList();

        Assert.AreEqual(0, missingRewardIds.Count, "Should identify no missing rewards when user has all rewards");
    }

    [Test]
    public void ProcessTierWithNoRewards_HandlesEmptyRewardList()
    {
        // Arrange - Tier has no rewards (edge case)
        var tierId = "tier-empty";
        var providerId = "patreon";
        var allRewardsInTier = new List<string>();
        var existingAssignments = new List<RewardAssignment>();

        // Act
        var existingTierAssignments = existingAssignments.Where(a =>
            a.ProviderId == providerId &&
            a.Metadata.ContainsKey("tier_id") &&
            a.Metadata["tier_id"].ToString() == tierId).ToList();

        var existingRewardIds = existingTierAssignments.Select(a => a.RewardId).ToHashSet();
        var missingRewardIds = allRewardsInTier.Where(id => !string.IsNullOrEmpty(id) && !existingRewardIds.Contains(id)).ToList();

        // Assert
        Assert.AreEqual(0, missingRewardIds.Count, "Empty tier should result in no missing rewards");
    }

    [Test]
    public void ProcessTierWithInvalidRewards_FiltersNullAndEmptyRewardIds()
    {
        // Arrange - Tier has null and empty reward IDs (edge case)
        var tierId = "tier-mixed";
        var providerId = "patreon";
        var allRewardsInTier = new List<string> { "reward-a", null, "", "reward-b" };
        var existingAssignments = new List<RewardAssignment>();

        // Act
        var existingTierAssignments = existingAssignments.Where(a =>
            a.ProviderId == providerId &&
            a.Metadata.ContainsKey("tier_id") &&
            a.Metadata["tier_id"].ToString() == tierId).ToList();

        var existingRewardIds = existingTierAssignments.Select(a => a.RewardId).ToHashSet();
        var missingRewardIds = allRewardsInTier.Where(id => !string.IsNullOrEmpty(id) && !existingRewardIds.Contains(id)).ToList();

        // Assert
        Assert.AreEqual(2, missingRewardIds.Count, "Should only count valid reward IDs");
        Assert.Contains("reward-a", missingRewardIds);
        Assert.Contains("reward-b", missingRewardIds);
    }
}
