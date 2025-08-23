using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Events;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
using W3ChampionsStatisticService.Hubs;
using W3ChampionsStatisticService.Rewards.Providers.Patreon;
using W3ChampionsStatisticService.Rewards.Services;

namespace WC3ChampionsStatisticService.Tests.Rewards;

[TestFixture]
public class PatreonDriftSyncTests
{
    private Mock<PatreonApiClient> _mockPatreonApiClient;
    private Mock<IProductMappingUserAssociationRepository> _mockAssociationRepository;
    private Mock<IProductMappingRepository> _mockProductMappingRepository;
    private Mock<IPatreonAccountLinkRepository> _mockPatreonLinkRepository;
    private Mock<IProductMappingReconciliationService> _mockReconciliationService;
    private PatreonDriftDetectionService _service;
    private PatreonOAuthService _oauthService;

    [SetUp]
    public void Setup()
    {
        _mockPatreonApiClient = new Mock<PatreonApiClient>(Mock.Of<System.Net.Http.HttpClient>());
        _mockAssociationRepository = new Mock<IProductMappingUserAssociationRepository>();
        _mockProductMappingRepository = new Mock<IProductMappingRepository>();
        _mockPatreonLinkRepository = new Mock<IPatreonAccountLinkRepository>();

        // Setup mock to return a linked account for the test PatreonUserId
        _mockPatreonLinkRepository.Setup(x => x.GetByPatreonUserId("a1b2c3d4-e5f6-7890-abcd-ef1234567890"))
            .ReturnsAsync(new PatreonAccountLink("TestBattleTag#1234", "a1b2c3d4-e5f6-7890-abcd-ef1234567890"));

        _mockReconciliationService = new Mock<IProductMappingReconciliationService>();

        // Setup the ReconcileUserAssociations method with eventIdPrefix parameter
        _mockReconciliationService.Setup(x => x.ReconcileUserAssociations(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>()))
            .ReturnsAsync(new ProductMappingReconciliationResult
            {
                Success = true,
                RewardsAdded = 1,
                RewardsRevoked = 0,
                TotalUsersAffected = 1
            });

        // Setup product mapping repository for drift sync
        _mockProductMappingRepository.Setup(x => x.GetByProviderAndProductId("patreon", "tier1"))
            .ReturnsAsync(new List<ProductMapping>
            {
                new ProductMapping
                {
                    Id = "mapping-123",
                    ProductName = "Tier 1",
                    RewardIds = new List<string> { "reward-1" },
                    ProductProviders = new List<ProductProviderPair>
                    {
                        new ProductProviderPair { ProviderId = "patreon", ProductId = "tier1" }
                    }
                }
            });

        // Setup association repository to return no existing associations
        _mockAssociationRepository.Setup(x => x.GetByUserAndProductMapping(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        // Setup association repository Create method
        _mockAssociationRepository.Setup(x => x.Create(It.IsAny<ProductMappingUserAssociation>()))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        _service = new PatreonDriftDetectionService(
            _mockPatreonApiClient.Object,
            _mockAssociationRepository.Object,
            _mockProductMappingRepository.Object,
            _mockPatreonLinkRepository.Object,
            _mockReconciliationService.Object);

        // Setup OAuth service for unlinking tests
        var mockHttpClient = new Mock<HttpClient>();
        var mockLogger = new Mock<ILogger<PatreonOAuthService>>();

        _oauthService = new PatreonOAuthService(
            mockHttpClient.Object,
            _mockPatreonLinkRepository.Object,
            _mockAssociationRepository.Object,
            _mockReconciliationService.Object,
            mockLogger.Object);
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
        Assert.AreEqual(0, syncResult.ProcessedAssociations.Count); // No processing in dry run

        // Verify no actual association changes happened in dry run
        _mockAssociationRepository.Verify(x => x.Create(It.IsAny<ProductMappingUserAssociation>()), Times.Never);
        _mockAssociationRepository.Verify(x => x.Update(It.IsAny<ProductMappingUserAssociation>()), Times.Never);
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
        Assert.AreEqual(0, syncResult.ProcessedAssociations.Count);
        Assert.AreEqual(0, syncResult.Errors.Count);

        // Verify no association changes happened
        _mockAssociationRepository.Verify(x => x.Create(It.IsAny<ProductMappingUserAssociation>()), Times.Never);
        _mockAssociationRepository.Verify(x => x.Update(It.IsAny<ProductMappingUserAssociation>()), Times.Never);
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

        // Verify actual association processing happened
        _mockPatreonLinkRepository.Verify(x => x.GetByPatreonUserId("a1b2c3d4-e5f6-7890-abcd-ef1234567890"), Times.Once);

        // The exact verification depends on the association creation logic
        // This test may need more setup for product mappings
    }

    [Test]
    public async Task UnlinkAccount_WithActiveAssociations_RevokesAssociationsAndRemovesLink()
    {
        // Arrange
        const string battleTag = "TestUser#1234";
        const string patreonUserId = "patreon-user-123";

        var activeAssociations = new List<ProductMappingUserAssociation>
        {
            new ProductMappingUserAssociation
            {
                Id = "assoc1",
                UserId = battleTag,
                ProviderId = "patreon",
                ProviderProductId = "tier1",
                ProductMappingId = "mapping1",
                Status = AssociationStatus.Active,
                AssignedAt = DateTime.UtcNow.AddDays(-1)
            },
            new ProductMappingUserAssociation
            {
                Id = "assoc2",
                UserId = battleTag,
                ProviderId = "patreon",
                ProviderProductId = "tier2",
                ProductMappingId = "mapping2",
                Status = AssociationStatus.Active,
                AssignedAt = DateTime.UtcNow.AddDays(-2)
            },
            new ProductMappingUserAssociation
            {
                Id = "assoc3",
                UserId = battleTag,
                ProviderId = "kofi", // Different provider - should not be touched
                ProviderProductId = "kofi-tier1",
                ProductMappingId = "mapping3",
                Status = AssociationStatus.Active,
                AssignedAt = DateTime.UtcNow.AddDays(-1)
            }
        };

        // Setup mocks
        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(battleTag))
            .ReturnsAsync(activeAssociations);

        _mockPatreonLinkRepository.Setup(x => x.RemoveByBattleTag(battleTag))
            .ReturnsAsync(true);

        var reconciliationResult = new ProductMappingReconciliationResult
        {
            Success = true,
            RewardsAdded = 0,
            RewardsRevoked = 2,
            TotalUsersAffected = 1
        };

        _mockReconciliationService.Setup(x => x.ReconcileUserAssociations(battleTag, It.IsAny<string>(), false))
            .ReturnsAsync(reconciliationResult);

        // Act
        var result = await _oauthService.UnlinkAccount(battleTag);

        // Assert
        Assert.IsTrue(result, "Unlink operation should succeed");

        // Verify that only Patreon associations were revoked
        var patreonAssociations = activeAssociations.Where(a => a.ProviderId == "patreon").ToList();
        foreach (var association in patreonAssociations)
        {
            Assert.AreEqual(AssociationStatus.Revoked, association.Status,
                $"Patreon association {association.Id} should be revoked");
            Assert.IsTrue(association.Metadata.ContainsKey("revocation_reason"),
                "Revoked association should have revocation reason in metadata");
            Assert.AreEqual("Account unlinked by user", association.Metadata["revocation_reason"],
                "Revocation reason should be set correctly");
        }

        // Verify Ko-Fi association was not touched
        var kofiAssociation = activeAssociations.First(a => a.ProviderId == "kofi");
        Assert.AreEqual(AssociationStatus.Active, kofiAssociation.Status,
            "Non-Patreon associations should remain active");

        // Verify repository calls
        _mockAssociationRepository.Verify(x => x.GetProductMappingsByUserId(battleTag), Times.Once,
            "Should fetch user associations");

        _mockAssociationRepository.Verify(x => x.Update(It.Is<ProductMappingUserAssociation>(a =>
            a.ProviderId == "patreon" && a.Status == AssociationStatus.Revoked)),
            Times.Exactly(2), "Should update both Patreon associations");

        _mockAssociationRepository.Verify(x => x.Update(It.Is<ProductMappingUserAssociation>(a =>
            a.ProviderId == "kofi")),
            Times.Never, "Should not update Ko-Fi associations");

        _mockReconciliationService.Verify(x => x.ReconcileUserAssociations(battleTag, It.IsAny<string>(), false), Times.Once,
            "Should trigger reconciliation to remove reward assignments");

        _mockPatreonLinkRepository.Verify(x => x.RemoveByBattleTag(battleTag), Times.Once,
            "Should remove the Patreon account link");
    }

    [Test]
    public async Task UnlinkAccount_WithNoAssociations_OnlyRemovesLink()
    {
        // Arrange
        const string battleTag = "TestUser#5678";

        var emptyAssociations = new List<ProductMappingUserAssociation>();

        // Setup mocks
        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(battleTag))
            .ReturnsAsync(emptyAssociations);

        _mockPatreonLinkRepository.Setup(x => x.RemoveByBattleTag(battleTag))
            .ReturnsAsync(true);

        // Act
        var result = await _oauthService.UnlinkAccount(battleTag);

        // Assert
        Assert.IsTrue(result, "Unlink operation should succeed");

        // Verify repository calls
        _mockAssociationRepository.Verify(x => x.GetProductMappingsByUserId(battleTag), Times.Once,
            "Should fetch user associations");

        _mockAssociationRepository.Verify(x => x.Update(It.IsAny<ProductMappingUserAssociation>()),
            Times.Never, "Should not update any associations when none exist");

        _mockReconciliationService.Verify(x => x.ReconcileUserAssociations(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
            Times.Never, "Should not trigger reconciliation when no associations exist");

        _mockPatreonLinkRepository.Verify(x => x.RemoveByBattleTag(battleTag), Times.Once,
            "Should still remove the Patreon account link");
    }

    [Test]
    public async Task UnlinkAccount_WithOnlyRevokedAssociations_OnlyRemovesLink()
    {
        // Arrange
        const string battleTag = "TestUser#9999";

        var revokedAssociations = new List<ProductMappingUserAssociation>
        {
            new ProductMappingUserAssociation
            {
                Id = "assoc1",
                UserId = battleTag,
                ProviderId = "patreon",
                Status = AssociationStatus.Revoked, // Already revoked
                AssignedAt = DateTime.UtcNow.AddDays(-1)
            }
        };

        // Setup mocks
        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(battleTag))
            .ReturnsAsync(revokedAssociations);

        _mockPatreonLinkRepository.Setup(x => x.RemoveByBattleTag(battleTag))
            .ReturnsAsync(true);

        // Act
        var result = await _oauthService.UnlinkAccount(battleTag);

        // Assert
        Assert.IsTrue(result, "Unlink operation should succeed");

        // Verify repository calls
        _mockAssociationRepository.Verify(x => x.GetProductMappingsByUserId(battleTag), Times.Once,
            "Should fetch user associations");

        _mockAssociationRepository.Verify(x => x.Update(It.IsAny<ProductMappingUserAssociation>()),
            Times.Never, "Should not update already revoked associations");

        _mockReconciliationService.Verify(x => x.ReconcileUserAssociations(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
            Times.Never, "Should not trigger reconciliation when no associations exist");

        _mockPatreonLinkRepository.Verify(x => x.RemoveByBattleTag(battleTag), Times.Once,
            "Should remove the Patreon account link");
    }
}
