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
/// A testable subclass that overrides LastRun so tests can inject an arbitrary
/// snapshot without running the actual background cycle.
/// </summary>
internal sealed class StubDriftBackgroundService : RewardDriftDetectionBackgroundService
{
    private DriftRunSnapshot _stub;

    public StubDriftBackgroundService()
        : base(
            new ServiceCollection().BuildServiceProvider(),
            Mock.Of<ILogger<RewardDriftDetectionBackgroundService>>())
    {
    }

    public override DriftRunSnapshot LastRun => _stub;

    public void SetLastRun(DriftRunSnapshot snapshot)
    {
        _stub = snapshot;
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
            bgService.SetLastRun(new DriftRunSnapshot(
                StartedAtUtc: started,
                CompletedAtUtc: completed,
                Succeeded: true,
                ErrorMessage: null,
                MembersAdded: 3,
                AssignmentsRevoked: 1,
                TiersUpdated: 2));

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
        // No last-run snapshot set — LastRun returns null

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
        Assert.That(json, Does.Contain("\"lastRun\":null"), "No cycle run yet — lastRun should be null");
    }

    [Test]
    public void GetDriftDetectionStatus_FailedCycle_ExposesErrorMessageAndZeroCounts()
    {
        // Arrange
        var started = DateTime.UtcNow.AddMinutes(-5);
        var completed = DateTime.UtcNow.AddMinutes(-4);

        var bgService = new StubDriftBackgroundService();
        bgService.SetLastRun(new DriftRunSnapshot(
            StartedAtUtc: started,
            CompletedAtUtc: completed,
            Succeeded: false,
            ErrorMessage: "Patreon API timeout",
            MembersAdded: 0,
            AssignmentsRevoked: 0,
            TiersUpdated: 0));

        var controller = BuildController(bgService);

        // Act
        var actionResult = controller.GetDriftDetectionStatus() as OkObjectResult;

        // Assert
        Assert.That(actionResult, Is.Not.Null, "Expected OkObjectResult");

        var json = JsonSerializer.Serialize(actionResult!.Value);

        Assert.That(json, Does.Contain("\"succeeded\":false"), "lastRun.succeeded should be false");
        Assert.That(json, Does.Contain("\"errorMessage\":\"Patreon API timeout\""), "lastRun.errorMessage should carry exception message");
        Assert.That(json, Does.Contain("\"membersAdded\":0"), "lastRun.membersAdded must be 0 on failure — no stale counts");
        Assert.That(json, Does.Contain("\"assignmentsRevoked\":0"), "lastRun.assignmentsRevoked must be 0 on failure");
        Assert.That(json, Does.Contain("\"tiersUpdated\":0"), "lastRun.tiersUpdated must be 0 on failure");
    }
}
