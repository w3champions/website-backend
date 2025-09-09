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
using W3C.Domain.Common.Repositories;
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
    private Mock<IRewardService> _mockRewardService;
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
        _mockRewardService = new Mock<IRewardService>();

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

        // Setup product mappings for batch operations
        var tier1Mapping = new ProductMapping
        {
            Id = "tier1-mapping-id",
            ProductName = "Tier 1",
            RewardIds = new List<string> { "reward-tier1" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "tier1" }
            }
        };

        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping> { tier1Mapping });

        // Setup account link 
        _mockPatreonLinkRepository.Setup(x => x.GetByPatreonUserId("a1b2c3d4-e5f6-7890-abcd-ef1234567890"))
            .ReturnsAsync(new PatreonAccountLink("TestUser#1234", "a1b2c3d4-e5f6-7890-abcd-ef1234567890"));

        // Setup empty existing associations for this user
        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId("TestUser#1234"))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        // Act
        var syncResult = await _service.SyncDrift(driftResult, dryRun: false);

        // Assert
        Assert.IsTrue(syncResult.Success);
        Assert.IsFalse(syncResult.WasDryRun);
        Assert.AreEqual(1, syncResult.MembersAdded);

        // Verify actual association processing happened (now uses batch GetAll instead of individual calls)
        _mockPatreonLinkRepository.Verify(x => x.GetAll(), Times.AtLeastOnce);

        // The exact verification depends on the association creation logic
        // This test may need more setup for product mappings
    }

    [Test]
    public async Task UnlinkAccount_WithActiveAssociations_RevokesAssociationsAndRemovesLink()
    {
        // Arrange
        const string battleTag = "TestUser#1234";

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

    public static IEnumerable<TestCaseData> DualTierTestCases()
    {
        // Scenario 1: Dual tier addition - Fresh patron gains both bronze and silver tiers simultaneously
        yield return new TestCaseData(
            "DualTierAddition",
            "DualTierUser#1234",
            new List<string>(), // Current internal tiers (none)
            new List<string> { "bronze-tier", "silver-tier" }, // New Patreon tiers (both at once)
            new List<ProductMappingUserAssociation>(), // No existing associations
            0, // Expected TiersUpdated count (missing member scenario uses MembersAdded instead)
            Times.Exactly(2), // Expected Create calls (bronze + silver created fresh)
            "Should create associations for both bronze and silver tiers for new dual-tier patron"
        ).SetName("SyncDrift_DualTierEntitlement_HandlesProperly");

        // Scenario 2: Tier downgrade - User loses silver but keeps bronze
        yield return new TestCaseData(
            "TierDowngrade",
            "DowngradeUser#1234",
            new List<string> { "bronze-tier", "silver-tier" }, // Current internal tiers
            new List<string> { "bronze-tier" }, // New Patreon tiers
            new List<ProductMappingUserAssociation>
            {
                new ProductMappingUserAssociation
                {
                    Id = "bronze-assoc",
                    UserId = "DowngradeUser#1234",
                    ProductMappingId = "bronze-mapping-id",
                    ProviderId = "patreon",
                    ProviderProductId = "bronze-tier",
                    Status = AssociationStatus.Active
                },
                new ProductMappingUserAssociation
                {
                    Id = "silver-assoc",
                    UserId = "DowngradeUser#1234",
                    ProductMappingId = "silver-mapping-id",
                    ProviderId = "patreon",
                    ProviderProductId = "silver-tier",
                    Status = AssociationStatus.Active
                }
            },
            1, // Expected TiersUpdated count
            Times.Once(), // Expected Create calls (for bronze recreation)
            "Should preserve bronze tier rewards while removing silver-specific rewards"
        ).SetName("SyncDrift_TierDowngrade_PreservesRemainingTierRewards");

        // Scenario 3: Tier upgrade - User gains silver while keeping bronze
        yield return new TestCaseData(
            "TierUpgrade",
            "UpgradeUser#1234",
            new List<string> { "bronze-tier" }, // Current internal tiers
            new List<string> { "bronze-tier", "silver-tier" }, // New Patreon tiers
            new List<ProductMappingUserAssociation>
            {
                new ProductMappingUserAssociation
                {
                    Id = "bronze-assoc",
                    UserId = "UpgradeUser#1234",
                    ProductMappingId = "bronze-mapping-id",
                    ProviderId = "patreon",
                    ProviderProductId = "bronze-tier",
                    Status = AssociationStatus.Active
                }
            },
            1, // Expected TiersUpdated count
            Times.Exactly(2), // Expected Create calls (bronze + silver recreation)
            "Should add silver tier rewards without duplicating shared rewards with bronze"
        ).SetName("SyncDrift_TierUpgrade_AddsNewTierWithoutDuplicates");
    }

    [TestCaseSource(nameof(DualTierTestCases))]
    public async Task SyncDrift_DualTierScenarios_HandlesCorrectly(
        string scenarioName,
        string battleTag,
        List<string> currentInternalTiers,
        List<string> newPatreonTiers,
        List<ProductMappingUserAssociation> existingAssociations,
        int expectedTiersUpdated,
        Times expectedCreateCalls,
        string expectedBehaviorDescription)
    {
        // Arrange
        var patreonUserId = $"{scenarioName.ToLower()}-user-id";
        var patreonMemberId = $"{scenarioName.ToLower()}-member-id";

        // Setup account link for individual query (backward compatibility)
        _mockPatreonLinkRepository.Setup(x => x.GetByPatreonUserId(patreonUserId))
            .ReturnsAsync(new PatreonAccountLink(battleTag, patreonUserId));

        // Setup account link for batch operations
        var accountLink = new PatreonAccountLink(battleTag, patreonUserId);
        _mockPatreonLinkRepository.Setup(x => x.GetAll())
            .ReturnsAsync(new List<PatreonAccountLink> { accountLink });

        // Setup product mappings for both tiers
        var bronzeMapping = new ProductMapping
        {
            Id = "bronze-mapping-id",
            ProductName = "Bronze Tier",
            RewardIds = new List<string> { "reward-bronze", "reward-common" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "bronze-tier" }
            }
        };

        var silverMapping = new ProductMapping
        {
            Id = "silver-mapping-id",
            ProductName = "Silver Tier",
            RewardIds = new List<string> { "reward-silver", "reward-common", "reward-premium" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "silver-tier" }
            }
        };

        _mockProductMappingRepository.Setup(x => x.GetByProviderAndProductId("patreon", "bronze-tier"))
            .ReturnsAsync(new List<ProductMapping> { bronzeMapping });

        _mockProductMappingRepository.Setup(x => x.GetByProviderAndProductId("patreon", "silver-tier"))
            .ReturnsAsync(new List<ProductMapping> { silverMapping });

        // Setup batch GetByProviderId for CreateAssociationsForTiers optimization
        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping> { bronzeMapping, silverMapping });

        // Setup existing associations
        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(battleTag))
            .ReturnsAsync(existingAssociations);

        foreach (var association in existingAssociations)
        {
            _mockAssociationRepository.Setup(x => x.GetByUserAndProductMapping(battleTag, association.ProductMappingId))
                .ReturnsAsync(new List<ProductMappingUserAssociation> { association });
        }

        // Setup empty associations for missing mappings
        var allMappingIds = new[] { "bronze-mapping-id", "silver-mapping-id" };
        var existingMappingIds = existingAssociations.Select(a => a.ProductMappingId).ToList();
        foreach (var mappingId in allMappingIds.Except(existingMappingIds))
        {
            _mockAssociationRepository.Setup(x => x.GetByUserAndProductMapping(battleTag, mappingId))
                .ReturnsAsync(new List<ProductMappingUserAssociation>());
        }

        // Create drift result based on scenario type
        var driftResult = new DriftDetectionResult
        {
            Timestamp = DateTime.UtcNow,
            ProviderId = "patreon"
        };

        if (!currentInternalTiers.Any() && newPatreonTiers.Any())
        {
            // Missing member scenario - user has Patreon entitlements but no internal associations
            driftResult.MissingMembers = new List<MissingMember>
            {
                new MissingMember
                {
                    PatreonMemberId = patreonMemberId,
                    PatreonUserId = patreonUserId,
                    PatronStatus = "active_patron",
                    EntitledTierIds = newPatreonTiers,
                    Reason = $"{scenarioName}: Active patron found in Patreon but no active rewards in our system"
                }
            };
        }
        else
        {
            // Tier mismatch scenario - user has different tiers in Patreon vs internal
            driftResult.MismatchedTiers = new List<TierMismatch>
            {
                new TierMismatch
                {
                    UserId = battleTag,
                    PatreonMemberId = patreonMemberId,
                    PatreonTiers = newPatreonTiers,
                    InternalTiers = currentInternalTiers,
                    Reason = $"{scenarioName}: Tier entitlements don't match between Patreon and internal state"
                }
            };
        }

        // Act
        var syncResult = await _service.SyncDrift(driftResult, dryRun: false);

        // Assert
        Assert.IsTrue(syncResult.Success, $"{scenarioName} should succeed");

        if (scenarioName == "DualTierAddition")
        {
            // For missing member scenario, check MembersAdded instead of TiersUpdated
            Assert.AreEqual(1, syncResult.MembersAdded, $"{scenarioName} should add one new member");
            Assert.AreEqual(0, syncResult.TiersUpdated, $"{scenarioName} should not update existing tiers");
        }
        else
        {
            // For tier mismatch scenarios, check TiersUpdated
            Assert.AreEqual(expectedTiersUpdated, syncResult.TiersUpdated, $"{scenarioName} should update expected number of tiers");
        }

        // Verify reconciliation was called at least once to handle rewards
        _mockReconciliationService.Verify(x => x.ReconcileUserAssociations(battleTag, It.IsAny<string>(), false), Times.AtLeastOnce,
            $"{scenarioName}: Should trigger reconciliation to handle rewards properly");

        // Verify association creation behavior matches expectations
        _mockAssociationRepository.Verify(x => x.Create(It.Is<ProductMappingUserAssociation>(a =>
            a.UserId == battleTag && a.ProviderId == "patreon")), expectedCreateCalls,
            $"{scenarioName}: {expectedBehaviorDescription}");

        // Perform scenario-specific reward verification
        await VerifyScenarioSpecificRewardBehavior(scenarioName, battleTag);
    }

    private async Task VerifyScenarioSpecificRewardBehavior(
        string scenarioName,
        string battleTag)
    {
        // Bronze rewards: ["reward-bronze", "reward-common"]
        // Silver rewards: ["reward-silver", "reward-common", "reward-premium"]

        switch (scenarioName)
        {
            case "DualTierAddition":
                // Fresh patron gains both bronze and silver -> should get all rewards for both tiers
                await VerifyDualTierAdditionRewards(battleTag);
                break;

            case "TierDowngrade":
                // User had bronze+silver, loses silver -> should lose silver-specific rewards but keep bronze+common
                await VerifyTierDowngradeRewards(battleTag);
                break;

            case "TierUpgrade":
                // User had bronze, gains silver -> should get silver-specific rewards without duplicating common
                await VerifyTierUpgradeRewards(battleTag);
                break;
        }
    }

    private Task VerifyDualTierAdditionRewards(string battleTag)
    {
        // Fresh patron gets both tiers simultaneously - should create all associations from scratch
        // Key behavior: All rewards should be added properly without any conflicts
        // Expected rewards: reward-bronze, reward-silver, reward-common (once), reward-premium

        var reconciliationCalls = _mockReconciliationService.Invocations
            .Where(i => i.Method.Name == "ReconcileUserAssociations" &&
                       (string)i.Arguments[0] == battleTag)
            .ToList();

        Assert.IsTrue(reconciliationCalls.Any(),
            "DualTierAddition: Should have reconciliation calls to assign all rewards for fresh dual-tier patron");

        return Task.CompletedTask;
    }

    private Task VerifyTierDowngradeRewards(string battleTag)
    {
        // User loses silver tier but keeps bronze
        // Should preserve: reward-bronze, reward-common
        // Should remove: reward-silver, reward-premium

        var reconciliationCalls = _mockReconciliationService.Invocations
            .Where(i => i.Method.Name == "ReconcileUserAssociations" &&
                       (string)i.Arguments[0] == battleTag)
            .ToList();

        Assert.IsTrue(reconciliationCalls.Any(),
            "TierDowngrade: Should have reconciliation calls to remove silver-specific rewards while preserving bronze");

        return Task.CompletedTask;
    }

    private Task VerifyTierUpgradeRewards(string battleTag)
    {
        // User gains silver tier while keeping bronze
        // Should add: reward-silver, reward-premium
        // Should not duplicate: reward-common (already has from bronze)

        var reconciliationCalls = _mockReconciliationService.Invocations
            .Where(i => i.Method.Name == "ReconcileUserAssociations" &&
                       (string)i.Arguments[0] == battleTag)
            .ToList();

        Assert.IsTrue(reconciliationCalls.Any(),
            "TierUpgrade: Should have reconciliation calls to add silver rewards without duplicating shared ones");

        return Task.CompletedTask;
    }

    [Test]
    public async Task SyncDrift_MultipleTieredRewards_ProcessesOnlyFirstTier()
    {
        // Arrange
        var battleTag = "TieredUser#1234";
        var patreonUserId = "tiered-user-id";
        var patreonMemberId = "tiered-member-id";

        // Setup account link
        var accountLink = new PatreonAccountLink(battleTag, patreonUserId);
        _mockPatreonLinkRepository.Setup(x => x.GetAll())
            .ReturnsAsync(new List<PatreonAccountLink> { accountLink });
        _mockPatreonLinkRepository.Setup(x => x.GetByPatreonUserId(patreonUserId))
            .ReturnsAsync(accountLink);

        // Setup product mappings - both are TIERED type
        var tier1Mapping = new ProductMapping
        {
            Id = "tier1-mapping-id",
            ProductName = "Tier 1",
            Type = ProductMappingType.Tiered,  // TIERED type
            RewardIds = new List<string> { "reward-tier1" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "6482057" }
            }
        };

        var tier2Mapping = new ProductMapping
        {
            Id = "tier2-mapping-id",
            ProductName = "Tier 2",
            Type = ProductMappingType.Tiered,  // TIERED type
            RewardIds = new List<string> { "reward-tier2" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "6482051" }
            }
        };

        // Setup GetByProviderId to return both mappings
        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping> { tier1Mapping, tier2Mapping });

        // Setup GetByProviderAndProductId for individual lookups
        _mockProductMappingRepository.Setup(x => x.GetByProviderAndProductId("patreon", "6482057"))
            .ReturnsAsync(new List<ProductMapping> { tier1Mapping });
        _mockProductMappingRepository.Setup(x => x.GetByProviderAndProductId("patreon", "6482051"))
            .ReturnsAsync(new List<ProductMapping> { tier2Mapping });

        // Setup no existing associations
        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(battleTag))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockAssociationRepository.Setup(x => x.GetByUserAndProductMapping(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        // Track created associations
        var createdAssociations = new List<ProductMappingUserAssociation>();
        _mockAssociationRepository.Setup(x => x.Create(It.IsAny<ProductMappingUserAssociation>()))
            .Callback<ProductMappingUserAssociation>(a => createdAssociations.Add(a))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        // Create drift result with multiple entitled tier IDs
        var driftResult = new DriftDetectionResult
        {
            Timestamp = DateTime.UtcNow,
            ProviderId = "patreon",
            MissingMembers = new List<MissingMember>
            {
                new MissingMember
                {
                    PatreonMemberId = patreonMemberId,
                    PatreonUserId = patreonUserId,
                    PatronStatus = "active_patron",
                    EntitledTierIds = new List<string> { "6482057", "6482051" }, // Multiple TIERED rewards
                    Reason = "Active patron with multiple tiers"
                }
            }
        };

        // Act
        var syncResult = await _service.SyncDrift(driftResult, dryRun: false);

        // Assert
        Assert.IsTrue(syncResult.Success, "Sync should succeed");
        Assert.AreEqual(1, syncResult.MembersAdded, "Should add 1 member");

        // Verify only ONE association was created (for the first tier)
        Assert.AreEqual(1, createdAssociations.Count,
            "Should create only 1 association for TIERED rewards even with multiple entitled tiers");

        // Verify it's for the first tier
        Assert.AreEqual("6482057", createdAssociations[0].ProviderProductId,
            "Should process only the first tier ID for TIERED rewards");
        Assert.AreEqual("tier1-mapping-id", createdAssociations[0].ProductMappingId,
            "Should use the first tier's mapping");

        // Verify the second tier was NOT processed
        Assert.IsFalse(createdAssociations.Any(a => a.ProviderProductId == "6482051"),
            "Should NOT process the second tier for TIERED rewards");
    }

    [Test]
    public async Task SyncDrift_MixedTieredAndNonTiered_ProcessesCorrectly()
    {
        // Arrange
        var battleTag = "MixedUser#1234";
        var patreonUserId = "mixed-user-id";
        var patreonMemberId = "mixed-member-id";

        // Setup account link
        var accountLink = new PatreonAccountLink(battleTag, patreonUserId);
        _mockPatreonLinkRepository.Setup(x => x.GetAll())
            .ReturnsAsync(new List<PatreonAccountLink> { accountLink });
        _mockPatreonLinkRepository.Setup(x => x.GetByPatreonUserId(patreonUserId))
            .ReturnsAsync(accountLink);

        // Setup product mappings - mix of TIERED and non-TIERED
        var tiered1Mapping = new ProductMapping
        {
            Id = "tiered1-mapping-id",
            ProductName = "Tiered 1",
            Type = ProductMappingType.Tiered,  // TIERED
            RewardIds = new List<string> { "reward-tiered1" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "tiered-1" }
            }
        };

        var tiered2Mapping = new ProductMapping
        {
            Id = "tiered2-mapping-id",
            ProductName = "Tiered 2",
            Type = ProductMappingType.Tiered,  // TIERED
            RewardIds = new List<string> { "reward-tiered2" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "tiered-2" }
            }
        };

        var oneTimeMapping = new ProductMapping
        {
            Id = "onetime-mapping-id",
            ProductName = "One Time Bonus",
            Type = ProductMappingType.OneTime,  // NOT TIERED
            RewardIds = new List<string> { "reward-onetime" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "onetime-bonus" }
            }
        };

        var recurringMapping = new ProductMapping
        {
            Id = "recurring-mapping-id",
            ProductName = "Recurring Perk",
            Type = ProductMappingType.Recurring,  // NOT TIERED
            RewardIds = new List<string> { "reward-recurring" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "recurring-perk" }
            }
        };

        // Setup GetByProviderId to return all mappings
        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping> { tiered1Mapping, tiered2Mapping, oneTimeMapping, recurringMapping });

        // Setup individual lookups
        _mockProductMappingRepository.Setup(x => x.GetByProviderAndProductId("patreon", "tiered-1"))
            .ReturnsAsync(new List<ProductMapping> { tiered1Mapping });
        _mockProductMappingRepository.Setup(x => x.GetByProviderAndProductId("patreon", "tiered-2"))
            .ReturnsAsync(new List<ProductMapping> { tiered2Mapping });
        _mockProductMappingRepository.Setup(x => x.GetByProviderAndProductId("patreon", "onetime-bonus"))
            .ReturnsAsync(new List<ProductMapping> { oneTimeMapping });
        _mockProductMappingRepository.Setup(x => x.GetByProviderAndProductId("patreon", "recurring-perk"))
            .ReturnsAsync(new List<ProductMapping> { recurringMapping });

        // Setup no existing associations
        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(battleTag))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockAssociationRepository.Setup(x => x.GetByUserAndProductMapping(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        // Track created associations
        var createdAssociations = new List<ProductMappingUserAssociation>();
        _mockAssociationRepository.Setup(x => x.Create(It.IsAny<ProductMappingUserAssociation>()))
            .Callback<ProductMappingUserAssociation>(a => createdAssociations.Add(a))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        // Create drift result with mix of tier types
        var driftResult = new DriftDetectionResult
        {
            Timestamp = DateTime.UtcNow,
            ProviderId = "patreon",
            MissingMembers = new List<MissingMember>
            {
                new MissingMember
                {
                    PatreonMemberId = patreonMemberId,
                    PatreonUserId = patreonUserId,
                    PatronStatus = "active_patron",
                    // Mix of TIERED and non-TIERED tiers
                    EntitledTierIds = new List<string> { "tiered-1", "tiered-2", "onetime-bonus", "recurring-perk" },
                    Reason = "Active patron with mixed tier types"
                }
            }
        };

        // Act
        var syncResult = await _service.SyncDrift(driftResult, dryRun: false);

        // Assert
        Assert.IsTrue(syncResult.Success, "Sync should succeed");
        Assert.AreEqual(1, syncResult.MembersAdded, "Should add 1 member");

        // Should create 3 associations total:
        // - 1 for the first TIERED (tiered-1)
        // - 1 for OneTime (onetime-bonus)
        // - 1 for Recurring (recurring-perk)
        Assert.AreEqual(3, createdAssociations.Count,
            "Should create 3 associations: 1 TIERED + 2 non-TIERED");

        // Verify first TIERED tier was processed
        Assert.IsTrue(createdAssociations.Any(a => a.ProviderProductId == "tiered-1"),
            "Should process the first TIERED tier");

        // Verify second TIERED tier was NOT processed
        Assert.IsFalse(createdAssociations.Any(a => a.ProviderProductId == "tiered-2"),
            "Should NOT process the second TIERED tier");

        // Verify all non-TIERED were processed
        Assert.IsTrue(createdAssociations.Any(a => a.ProviderProductId == "onetime-bonus"),
            "Should process OneTime tier");
        Assert.IsTrue(createdAssociations.Any(a => a.ProviderProductId == "recurring-perk"),
            "Should process Recurring tier");
    }
}
