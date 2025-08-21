using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Moq;
using NUnit.Framework;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Events;
using W3C.Domain.Rewards.Repositories;
using W3ChampionsStatisticService.Hubs;
using W3ChampionsStatisticService.Rewards.Providers.Patreon;
using W3ChampionsStatisticService.Rewards.Services;

namespace WC3ChampionsStatisticService.Tests.Rewards;

[TestFixture]
public class PatreonDriftSyncTests
{
    private Mock<PatreonApiClient> _mockPatreonApiClient;
    private Mock<IRewardAssignmentRepository> _mockAssignmentRepository;
    private Mock<IRewardService> _mockRewardService;
    private PatreonDriftDetectionService _service;

    [SetUp]
    public void Setup()
    {
        _mockPatreonApiClient = new Mock<PatreonApiClient>(Mock.Of<System.Net.Http.HttpClient>());
        _mockAssignmentRepository = new Mock<IRewardAssignmentRepository>();
        _mockRewardService = new Mock<IRewardService>();
        var mockPatreonLinkRepository = new Mock<IPatreonAccountLinkRepository>();
        
        // Setup mock to return a linked account for the test PatreonUserId
        mockPatreonLinkRepository.Setup(x => x.GetByPatreonUserId("a1b2c3d4-e5f6-7890-abcd-ef1234567890"))
            .ReturnsAsync(new PatreonAccountLink("TestBattleTag#1234", "a1b2c3d4-e5f6-7890-abcd-ef1234567890"));
        
        _service = new PatreonDriftDetectionService(
            _mockPatreonApiClient.Object,
            _mockAssignmentRepository.Object,
            _mockRewardService.Object,
            mockPatreonLinkRepository.Object);
    }

    [Test]
    public async Task SyncDrift_DryRun_GeneratesEventsWithCorrectIdentification()
    {
        // Arrange
        var driftResult = new DriftDetectionResult
        {
            Timestamp = DateTime.UtcNow,
            ProviderId = "patreon",
            MissingMembers = new List<MissingMember>
            {
                new MissingMember
                {
                    PatreonMemberId = "member123",
                    PatreonUserId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
                    PatronStatus = "active_patron",
                    EntitledTierIds = new List<string> { "tier1" },
                    Reason = "Test missing member"
                }
            },
            ExtraAssignments = new List<ExtraAssignment>
            {
                new ExtraAssignment
                {
                    AssignmentId = "assignment456",
                    UserId = "TestUser#456",
                    RewardId = "reward1",
                    AssignedAt = DateTime.UtcNow.AddDays(-1),
                    Reason = "Test extra assignment"
                }
            },
            MismatchedTiers = new List<TierMismatch>
            {
                new TierMismatch
                {
                    UserId = "TestUser#789",
                    PatreonMemberId = "member789",
                    PatreonTiers = new List<string> { "tier2" },
                    InternalTiers = new List<string> { "tier1" },
                    Reason = "Test tier mismatch"
                }
            }
        };

        // Act
        var syncResult = await _service.SyncDrift(driftResult, dryRun: true);

        // Assert
        Assert.IsTrue(syncResult.Success);
        Assert.IsTrue(syncResult.WasDryRun);
        Assert.AreEqual(1, syncResult.MembersAdded);
        Assert.AreEqual(1, syncResult.AssignmentsRevoked);
        Assert.AreEqual(1, syncResult.TiersUpdated);
        Assert.AreEqual(3, syncResult.GeneratedEvents.Count);

        // Verify missing member event identification
        var missingMemberEvent = syncResult.GeneratedEvents[0];
        Assert.IsTrue(missingMemberEvent.EventId.StartsWith("drift-sync:patreon:"));
        Assert.IsTrue(missingMemberEvent.EventId.Contains("TestBattleTag#1234"));
        Assert.AreEqual("sync:member:member123", missingMemberEvent.ProviderReference);
        Assert.AreEqual("drift_sync", missingMemberEvent.Metadata["event_source"]);
        Assert.AreEqual("missing_member", missingMemberEvent.Metadata["sync_reason"]);
        Assert.AreEqual("member123", missingMemberEvent.Metadata["patreon_member_id"]);
        Assert.AreEqual("Test missing member", missingMemberEvent.Metadata["sync_reason_detail"]);

        // Verify extra assignment event identification
        var extraAssignmentEvent = syncResult.GeneratedEvents[1];
        Assert.IsTrue(extraAssignmentEvent.EventId.StartsWith("drift-sync:patreon:"));
        Assert.IsTrue(extraAssignmentEvent.EventId.Contains("TestUser#456"));
        Assert.AreEqual("sync:revoke:assignment456", extraAssignmentEvent.ProviderReference);
        Assert.AreEqual("drift_sync", extraAssignmentEvent.Metadata["event_source"]);
        Assert.AreEqual("extra_assignment", extraAssignmentEvent.Metadata["sync_reason"]);
        Assert.AreEqual("assignment456", extraAssignmentEvent.Metadata["original_assignment_id"]);

        // Verify tier mismatch event identification
        var tierMismatchEvent = syncResult.GeneratedEvents[2];
        Assert.IsTrue(tierMismatchEvent.EventId.StartsWith("drift-sync:patreon:"));
        Assert.IsTrue(tierMismatchEvent.EventId.Contains("TestUser#789"));
        Assert.IsTrue(tierMismatchEvent.ProviderReference.StartsWith("sync:tier-update:"));
        Assert.AreEqual("drift_sync", tierMismatchEvent.Metadata["event_source"]);
        Assert.AreEqual("tier_mismatch", tierMismatchEvent.Metadata["sync_reason"]);
        Assert.AreEqual("member789", tierMismatchEvent.Metadata["patreon_member_id"]);
        Assert.AreEqual("tier1", tierMismatchEvent.Metadata["previous_tiers"]);
        Assert.AreEqual("tier2", tierMismatchEvent.Metadata["new_tiers"]);

        // Verify no actual processing happened in dry run
        _mockRewardService.Verify(x => x.ProcessRewardEvent(It.IsAny<RewardEvent>()), Times.Never);
    }

    [Test]
    public async Task SyncDrift_NoDrift_ReturnsSuccessWithoutProcessing()
    {
        // Arrange
        var driftResult = new DriftDetectionResult
        {
            Timestamp = DateTime.UtcNow,
            ProviderId = "patreon",
            MissingMembers = new List<MissingMember>(),
            ExtraAssignments = new List<ExtraAssignment>(),
            MismatchedTiers = new List<TierMismatch>()
        };

        // Act
        var syncResult = await _service.SyncDrift(driftResult, dryRun: false);

        // Assert
        Assert.IsTrue(syncResult.Success);
        Assert.IsFalse(syncResult.WasDryRun);
        Assert.AreEqual(0, syncResult.MembersAdded);
        Assert.AreEqual(0, syncResult.AssignmentsRevoked);
        Assert.AreEqual(0, syncResult.TiersUpdated);
        Assert.AreEqual(0, syncResult.GeneratedEvents.Count);
        Assert.AreEqual(0, syncResult.Errors.Count);

        // Verify no processing happened
        _mockRewardService.Verify(x => x.ProcessRewardEvent(It.IsAny<RewardEvent>()), Times.Never);
    }

    [Test]
    public async Task SyncDrift_ActualRun_CallsRewardService()
    {
        // Arrange
        var driftResult = new DriftDetectionResult
        {
            Timestamp = DateTime.UtcNow,
            ProviderId = "patreon",
            MissingMembers = new List<MissingMember>
            {
                new MissingMember
                {
                    PatreonMemberId = "member123",
                    PatreonUserId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
                    PatronStatus = "active_patron",
                    EntitledTierIds = new List<string> { "tier1" },
                    Reason = "Test missing member"
                }
            }
        };

        // Act
        var syncResult = await _service.SyncDrift(driftResult, dryRun: false);

        // Assert
        Assert.IsTrue(syncResult.Success);
        Assert.IsFalse(syncResult.WasDryRun);
        Assert.AreEqual(1, syncResult.MembersAdded);

        // Verify actual processing happened
        _mockRewardService.Verify(x => x.ProcessRewardEvent(It.Is<RewardEvent>(e => 
            e.EventId.StartsWith("drift-sync:") && 
            e.Metadata.ContainsKey("event_source") &&
            e.Metadata["event_source"].ToString() == "drift_sync")), Times.Once);
    }
}