using System;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using W3C.Domain.Common.Services;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Repositories;
using W3ChampionsStatisticService.Rewards.BackgroundServices;
using W3ChampionsStatisticService.Rewards.Controllers;
using W3ChampionsStatisticService.Rewards.Providers.Patreon;
using W3ChampionsStatisticService.Rewards.Services;

namespace WC3ChampionsStatisticService.Tests.Rewards;

/// <summary>
/// A testable subclass that exposes a helper to preset the last-run state
/// without needing to run the actual background cycle.
/// </summary>
internal sealed class StubDriftBackgroundService : RewardDriftDetectionBackgroundService
{
    public StubDriftBackgroundService()
        : base(
            new ServiceCollection().BuildServiceProvider(),
            Mock.Of<ILogger<RewardDriftDetectionBackgroundService>>())
    {
    }

    public void SetLastRunState(
        DateTime? startedAtUtc,
        DateTime? completedAtUtc,
        bool succeeded,
        string errorMessage,
        int membersAdded,
        int assignmentsRevoked,
        int tiersUpdated)
    {
        // Use reflection to set the private setters (they are auto-properties
        // with private set — the simplest approach that avoids changing production code).
        var t = typeof(RewardDriftDetectionBackgroundService);
        t.GetProperty(nameof(LastRunStartedAtUtc))!.SetValue(this, startedAtUtc);
        t.GetProperty(nameof(LastRunCompletedAtUtc))!.SetValue(this, completedAtUtc);
        t.GetProperty(nameof(LastRunSucceeded))!.SetValue(this, succeeded);
        t.GetProperty(nameof(LastRunErrorMessage))!.SetValue(this, errorMessage);
        t.GetProperty(nameof(LastRunMembersAdded))!.SetValue(this, membersAdded);
        t.GetProperty(nameof(LastRunAssignmentsRevoked))!.SetValue(this, assignmentsRevoked);
        t.GetProperty(nameof(LastRunTiersUpdated))!.SetValue(this, tiersUpdated);
    }
}

[TestFixture]
public class RewardDriftDetectionControllerTests
{
    private static RewardDriftDetectionController BuildController(
        StubDriftBackgroundService bgService)
    {
        // PatreonApiClient requires an HttpClient in its constructor — pass a real (unused) one.
        var patreonApiClient = new Mock<PatreonApiClient>(new System.Net.Http.HttpClient());

        var patreonService = new PatreonDriftDetectionService(
            patreonApiClient.Object,
            Mock.Of<IProductMappingUserAssociationRepository>(),
            Mock.Of<IProductMappingRepository>(),
            Mock.Of<IPatreonAccountLinkRepository>(),
            Mock.Of<IProductMappingReconciliationService>(),
            Mock.Of<IRewardAssignmentRepository>(),
            Mock.Of<IRewardService>());

        return new RewardDriftDetectionController(
            patreonService,
            Mock.Of<IAuditLogService>(),
            Mock.Of<ILogger<RewardDriftDetectionController>>(),
            bgService);
    }

    [Test]
    public void GetDriftDetectionStatus_ReturnsEnvVarStateAndLastRunSummary()
    {
        // Arrange
        Environment.SetEnvironmentVariable("REWARDS_DRIFT_DETECTION_ENABLED", "true");
        Environment.SetEnvironmentVariable("REWARDS_DRIFT_AUTO_SYNC_ENABLED", "true");
        Environment.SetEnvironmentVariable("REWARDS_DRIFT_SYNC_DRY_RUN", "false");
        Environment.SetEnvironmentVariable("REWARDS_PATREON_IGNORED_TIER_IDS", "15145463");

        try
        {
            var started = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            var completed = new DateTime(2026, 1, 15, 10, 0, 5, DateTimeKind.Utc);

            var bgService = new StubDriftBackgroundService();
            bgService.SetLastRunState(
                startedAtUtc: started,
                completedAtUtc: completed,
                succeeded: true,
                errorMessage: null,
                membersAdded: 3,
                assignmentsRevoked: 1,
                tiersUpdated: 2);

            var controller = BuildController(bgService);

            // Act
            var actionResult = controller.GetDriftDetectionStatus() as OkObjectResult;

            // Assert
            Assert.That(actionResult, Is.Not.Null, "Expected OkObjectResult");

            var json = JsonSerializer.Serialize(actionResult!.Value);

            Assert.That(json, Does.Contain("\"detectionEnabled\":true"), "detectionEnabled should be true from env var");
            Assert.That(json, Does.Contain("\"autoSyncEnabled\":true"), "autoSyncEnabled should be true from env var");
            Assert.That(json, Does.Contain("\"dryRun\":false"), "dryRun should be false from env var");
            Assert.That(json, Does.Contain("\"ignoredTierIds\":\"15145463\""), "ignoredTierIds should reflect env var");
            Assert.That(json, Does.Contain("\"succeeded\":true"), "lastRun.succeeded should be true");
            Assert.That(json, Does.Contain("\"membersAdded\":3"), "lastRun.membersAdded should be 3");
            Assert.That(json, Does.Contain("\"assignmentsRevoked\":1"), "lastRun.assignmentsRevoked should be 1");
            Assert.That(json, Does.Contain("\"tiersUpdated\":2"), "lastRun.tiersUpdated should be 2");
            Assert.That(json, Does.Contain("\"patreon\""), "providers should include patreon");
        }
        finally
        {
            Environment.SetEnvironmentVariable("REWARDS_DRIFT_DETECTION_ENABLED", null);
            Environment.SetEnvironmentVariable("REWARDS_DRIFT_AUTO_SYNC_ENABLED", null);
            Environment.SetEnvironmentVariable("REWARDS_DRIFT_SYNC_DRY_RUN", null);
            Environment.SetEnvironmentVariable("REWARDS_PATREON_IGNORED_TIER_IDS", null);
        }
    }

    [Test]
    public void GetDriftDetectionStatus_WhenNoEnvVars_ReturnsConservativeDefaults()
    {
        // Arrange — ensure env vars are absent
        Environment.SetEnvironmentVariable("REWARDS_DRIFT_DETECTION_ENABLED", null);
        Environment.SetEnvironmentVariable("REWARDS_DRIFT_AUTO_SYNC_ENABLED", null);
        Environment.SetEnvironmentVariable("REWARDS_DRIFT_SYNC_DRY_RUN", null);
        Environment.SetEnvironmentVariable("REWARDS_PATREON_IGNORED_TIER_IDS", null);

        var bgService = new StubDriftBackgroundService();
        // No last-run state set — all properties at defaults (null / false / 0)

        var controller = BuildController(bgService);

        // Act
        var actionResult = controller.GetDriftDetectionStatus() as OkObjectResult;

        // Assert
        Assert.That(actionResult, Is.Not.Null, "Expected OkObjectResult");

        var json = JsonSerializer.Serialize(actionResult!.Value);

        Assert.That(json, Does.Contain("\"detectionEnabled\":false"), "Default: detection disabled");
        Assert.That(json, Does.Contain("\"autoSyncEnabled\":false"), "Default: auto-sync disabled");
        Assert.That(json, Does.Contain("\"dryRun\":true"), "Default: dry-run on for safety");
        Assert.That(json, Does.Contain("\"ignoredTierIds\":\"\""), "Default: empty ignored-tier list");
        Assert.That(json, Does.Contain("\"startedAtUtc\":null"), "No cycle run yet — startedAtUtc null");
        Assert.That(json, Does.Contain("\"completedAtUtc\":null"), "No cycle run yet — completedAtUtc null");
    }
}
