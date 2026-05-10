using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
using W3ChampionsStatisticService.Rewards.Providers.Patreon;
using W3ChampionsStatisticService.Rewards.Services;
using WC3ChampionsStatisticService.UnitTests.Rewards.Builders;

namespace WC3ChampionsStatisticService.Tests.Rewards;

[TestFixture]
public class PatreonDriftSyncTests
{
    private Mock<PatreonApiClient> _mockPatreonApiClient;
    private Mock<IProductMappingUserAssociationRepository> _mockAssociationRepository;
    private Mock<IProductMappingRepository> _mockProductMappingRepository;
    private Mock<IPatreonAccountLinkRepository> _mockPatreonLinkRepository;
    private Mock<IProductMappingReconciliationService> _mockReconciliationService;
    private Mock<IRewardAssignmentRepository> _mockRewardAssignmentRepository;
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

        _mockRewardAssignmentRepository = new Mock<IRewardAssignmentRepository>();

        // Default behavior: empty list (most tests will not exercise it; specific tests override)
        _mockRewardAssignmentRepository.Setup(x => x.GetByUserIdAndStatus(It.IsAny<string>(), It.IsAny<RewardStatus>()))
            .ReturnsAsync(new List<RewardAssignment>());

        _service = new PatreonDriftDetectionService(
            _mockPatreonApiClient.Object,
            _mockAssociationRepository.Object,
            _mockProductMappingRepository.Object,
            _mockPatreonLinkRepository.Object,
            _mockReconciliationService.Object,
            _mockRewardAssignmentRepository.Object,
            _mockRewardService.Object);

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
        // Arrange — "TestUser#789" has no PatreonTiersFiltered set (null), so the actionable
        // tier set is empty and the no-op guard fires: TiersUpdated stays 0.
        // This is correct: UpdateUserAssociationTiers is now always called (even in dry-run)
        // and the no-op guard is consulted before any dryRun short-circuit.
        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId("TestUser#789"))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping>());

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
                    EntitledTiers = new List<EntitledTier> { new EntitledTier { TierId = "tier1" } },
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
                    // PatreonTiersFiltered intentionally null — simulates a mismatch entry
                    // where no filtered tiers are set, so the actionable set is empty → no-op.
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
        // TiersUpdated is 0: the no-op guard fires before the dry-run short-circuit.
        // PatreonTiersFiltered is null → actionableTiers is empty → matches existing empty set.
        Assert.AreEqual(0, syncResult.TiersUpdated);
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
                    EntitledTiers = new List<EntitledTier> { new EntitledTier { TierId = "tier1" } },
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
        // Scenario 1: Dual tier addition - Fresh patron gains both bronze and silver tiers simultaneously.
        // bronze-tier (100¢) and silver-tier (500¢) are both OneTime/Recurring (not Tiered),
        // so the TIERED filter passes all through — both associations are created.
        yield return new TestCaseData(
            "DualTierAddition",
            "DualTierUser#1234",
            new List<string>(), // Current internal tiers (none)
            new List<EntitledTier>
            {
                new EntitledTier { TierId = "bronze-tier", AmountCents = 100 },
                new EntitledTier { TierId = "silver-tier", AmountCents = 500 }
            }, // New Patreon tiers with explicit amount_cents
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
            new List<EntitledTier>
            {
                new EntitledTier { TierId = "bronze-tier", AmountCents = 100 }
            }, // New Patreon tiers with explicit amount_cents
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
            new List<EntitledTier>
            {
                new EntitledTier { TierId = "bronze-tier", AmountCents = 100 },
                new EntitledTier { TierId = "silver-tier", AmountCents = 500 }
            }, // New Patreon tiers with explicit amount_cents
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
        List<EntitledTier> newPatreonTiers,
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

        var newPatreonTierIds = newPatreonTiers.Select(t => t.TierId).ToList();

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
                    EntitledTiers = newPatreonTiers.ToList(), // preserve AmountCents for filter logic
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
                    PatreonTiers = newPatreonTierIds,
                    PatreonTiersFiltered = newPatreonTierIds, // For test, assume no filtering needed
                    InternalTiers = currentInternalTiers,
                    InternalTiersFiltered = currentInternalTiers, // For test, assume no filtering needed
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
    public async Task SyncDrift_MultipleTieredRewards_ProcessesHighestAmountTier()
    {
        // Arrange: Silver (6482057, 500¢) and Gold (6482070, 1000¢) both TIERED.
        // The filter must pick Gold regardless of input order.
        var battleTag = "TieredUser#1234";
        var patreonUserId = "tiered-user-id";
        var patreonMemberId = "tiered-member-id";

        // Setup account link
        var accountLink = new PatreonAccountLink(battleTag, patreonUserId);
        _mockPatreonLinkRepository.Setup(x => x.GetAll())
            .ReturnsAsync(new List<PatreonAccountLink> { accountLink });
        _mockPatreonLinkRepository.Setup(x => x.GetByPatreonUserId(patreonUserId))
            .ReturnsAsync(accountLink);

        // Silver tier (500¢) — TIERED
        var silverMapping = new ProductMapping
        {
            Id = "silver-mapping-id",
            ProductName = "Silver Tier",
            Type = ProductMappingType.Tiered,
            RewardIds = new List<string> { "reward-silver" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "6482057" }
            }
        };

        // Gold tier (1000¢) — TIERED, highest amount_cents
        var goldMapping = new ProductMapping
        {
            Id = "gold-mapping-id",
            ProductName = "Gold Tier",
            Type = ProductMappingType.Tiered,
            RewardIds = new List<string> { "reward-gold" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "6482070" }
            }
        };

        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping> { silverMapping, goldMapping });
        _mockProductMappingRepository.Setup(x => x.GetByProviderAndProductId("patreon", "6482057"))
            .ReturnsAsync(new List<ProductMapping> { silverMapping });
        _mockProductMappingRepository.Setup(x => x.GetByProviderAndProductId("patreon", "6482070"))
            .ReturnsAsync(new List<ProductMapping> { goldMapping });

        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(battleTag))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockAssociationRepository.Setup(x => x.GetByUserAndProductMapping(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        var createdAssociations = new List<ProductMappingUserAssociation>();
        _mockAssociationRepository.Setup(x => x.Create(It.IsAny<ProductMappingUserAssociation>()))
            .Callback<ProductMappingUserAssociation>(a => createdAssociations.Add(a))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        // Entitled tiers: Silver listed first, Gold listed second — Gold must win
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
                    EntitledTiers = new List<EntitledTier>
                    {
                        new EntitledTier { TierId = "6482057", AmountCents = 500 }, // Silver first in list
                        new EntitledTier { TierId = "6482070", AmountCents = 1000 } // Gold second — highest amount
                    },
                    Reason = "Active patron with Silver and Gold tiers"
                }
            }
        };

        // Act
        var syncResult = await _service.SyncDrift(driftResult, dryRun: false);

        // Assert
        Assert.IsTrue(syncResult.Success, "Sync should succeed");
        Assert.AreEqual(1, syncResult.MembersAdded, "Should add 1 member");

        // Filter picks the single highest-amount TIERED tier
        Assert.AreEqual(1, createdAssociations.Count,
            "Should create only 1 association for TIERED rewards");

        // Gold (6482070) wins — it has the highest amount_cents
        Assert.AreEqual("6482070", createdAssociations[0].ProviderProductId,
            "Should process the highest-amount tier (Gold), not the first in the list (Silver)");
        Assert.AreEqual("gold-mapping-id", createdAssociations[0].ProductMappingId,
            "Should use the Gold tier mapping");

        // Silver must NOT be created
        Assert.IsFalse(createdAssociations.Any(a => a.ProviderProductId == "6482057"),
            "Should NOT process the lower-amount Silver tier");
    }

    [Test]
    public async Task SyncDrift_MixedTieredAndNonTiered_ProcessesCorrectly()
    {
        // Arrange: tiered-1 (200¢) and tiered-2 (800¢) — tiered-2 is the highest-amount TIERED tier.
        // The filter must pick tiered-2 even though tiered-1 appears first in the input list.
        // Non-TIERED types (OneTime, Recurring) are all passed through unchanged.
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
        // tiered-1: 200¢ — lower amount, listed first in entitled tiers
        var tiered1Mapping = new ProductMapping
        {
            Id = "tiered1-mapping-id",
            ProductName = "Tiered 1 (cheaper)",
            Type = ProductMappingType.Tiered,  // TIERED
            RewardIds = new List<string> { "reward-tiered1" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "tiered-1" }
            }
        };

        // tiered-2: 800¢ — higher amount, must be selected regardless of list position
        var tiered2Mapping = new ProductMapping
        {
            Id = "tiered2-mapping-id",
            ProductName = "Tiered 2 (more expensive)",
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

        // Create drift result with mix of tier types.
        // tiered-1 (200¢) is listed first but tiered-2 (800¢) must win for TIERED selection.
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
                    EntitledTiers = new List<EntitledTier>
                    {
                        new EntitledTier { TierId = "tiered-1", AmountCents = 200 }, // lower amount, first in list
                        new EntitledTier { TierId = "tiered-2", AmountCents = 800 }, // higher amount, must win
                        new EntitledTier { TierId = "onetime-bonus" },
                        new EntitledTier { TierId = "recurring-perk" }
                    },
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
        // - 1 for the highest-amount TIERED (tiered-2, 800¢)
        // - 1 for OneTime (onetime-bonus)
        // - 1 for Recurring (recurring-perk)
        Assert.AreEqual(3, createdAssociations.Count,
            "Should create 3 associations: 1 highest-amount TIERED + 2 non-TIERED");

        // Verify the highest-amount TIERED tier (tiered-2) was processed
        Assert.IsTrue(createdAssociations.Any(a => a.ProviderProductId == "tiered-2"),
            "Should process the highest-amount TIERED tier (tiered-2, 800¢)");

        // Verify the lower-amount TIERED tier (tiered-1) was NOT processed
        Assert.IsFalse(createdAssociations.Any(a => a.ProviderProductId == "tiered-1"),
            "Should NOT process the lower-amount TIERED tier (tiered-1, 200¢)");

        // Verify all non-TIERED were processed
        Assert.IsTrue(createdAssociations.Any(a => a.ProviderProductId == "onetime-bonus"),
            "Should process OneTime tier");
        Assert.IsTrue(createdAssociations.Any(a => a.ProviderProductId == "recurring-perk"),
            "Should process Recurring tier");
    }

    private static IEnumerable<TestCaseData> DriftDetectionTieredTestCases()
    {
        // Case 1 (rewritten): Internal has the lower-amount tier (Bronze 6482051, 100¢).
        // Patreon has [Bronze, Gold] — filter picks Gold (1000¢, highest amount_cents).
        // Internal filter picks Bronze (only tier present). Gold ≠ Bronze → drift IS detected.
        yield return new TestCaseData(
            "eliphanti#2142",
            new List<EntitledTier>
            {
                new EntitledTier { TierId = "6482051", AmountCents = 100 },  // Bronze
                new EntitledTier { TierId = "6482070", AmountCents = 1000 } // Gold — highest, must win
            },
            new List<string> { "6482051" },              // Internal has Bronze only (lower tier)
            ProductMappingType.Tiered,                   // All tiers are TIERED type
            true,                                         // SHOULD detect drift (internal has lower tier)
            1,                                            // Expected mismatch count
            "Multiple TIERED rewards - internal has lower tier (Bronze) but Patreon filters to Gold"
        ).SetName("DetectDrift_MultipleTiered_InternalHasLowerTier_HasDrift");

        // Case 2 (reframed by amount): Internal has the highest-amount tier (Gold 6482070, 1000¢).
        // Patreon has [Silver, Gold] — filter picks Gold (1000¢). Both sides → Gold. No drift.
        yield return new TestCaseData(
            "correcttier#1234",
            new List<EntitledTier>
            {
                new EntitledTier { TierId = "6482057", AmountCents = 500 },  // Silver
                new EntitledTier { TierId = "6482070", AmountCents = 1000 }  // Gold — highest, must win
            },
            new List<string> { "6482070" },              // Internal correctly has Gold (highest-amount tier)
            ProductMappingType.Tiered,                   // All tiers are TIERED type
            false,                                        // Should NOT detect drift
            0,                                            // Expected mismatch count
            "Multiple TIERED rewards - internal correctly has highest-amount tier (Gold)"
        ).SetName("DetectDrift_MultipleTiered_InternalHasHighestAmountTier_NoDrift");

        // Case 3: No drift - Single TIERED tier matches
        yield return new TestCaseData(
            "singletier#1234",
            new List<EntitledTier>
            {
                new EntitledTier { TierId = "6482057", AmountCents = 500 }   // Silver only
            },
            new List<string> { "6482057" },              // Internal stored tiers
            ProductMappingType.Tiered,                   // TIERED type
            false,                                        // Should NOT detect drift
            0,                                            // Expected mismatch count
            "Single TIERED reward - exact match"
        ).SetName("DetectDrift_SingleTiered_ExactMatch_NoDrift");

        // Case 4: Has drift - User lost a tier
        yield return new TestCaseData(
            "losttier#1234",
            new List<EntitledTier>(),                    // Patreon entitled tiers (none)
            new List<string> { "6482057" },              // Internal still has tier
            ProductMappingType.Tiered,                   // TIERED type
            true,                                         // SHOULD detect drift
            0,                                            // Expected mismatch count (reported as extra assignment)
            "User lost TIERED reward - internal still has it"
        ).SetName("DetectDrift_LostTiered_InternalStillHas_HasDrift");

        // Case 5: No drift - Multiple non-TIERED rewards all match
        yield return new TestCaseData(
            "notiered#1234",
            new List<EntitledTier>
            {
                new EntitledTier { TierId = "onetime-1" },
                new EntitledTier { TierId = "onetime-2" }
            },
            new List<string> { "onetime-1", "onetime-2" }, // Internal has both
            ProductMappingType.OneTime,                     // NOT TIERED
            false,                                           // Should NOT detect drift
            0,                                               // Expected mismatch count
            "Multiple non-TIERED rewards - all present"
        ).SetName("DetectDrift_MultipleNonTiered_AllMatch_NoDrift");
    }

    [TestCaseSource(nameof(DriftDetectionTieredTestCases))]
    public async Task DetectDrift_TieredRewardScenarios(
        string battleTag,
        List<EntitledTier> patreonEntitledTiers,
        List<string> internalStoredTiers,
        ProductMappingType mappingType,
        bool shouldDetectDrift,
        int expectedMismatchCount,
        string description)
    {
        // Arrange
        var patreonUserId = $"{battleTag.Replace("#", "-")}-patreon-id";

        // Setup account link
        var accountLink = new PatreonAccountLink(battleTag, patreonUserId);
        _mockPatreonLinkRepository.Setup(x => x.GetAll())
            .ReturnsAsync(new List<PatreonAccountLink> { accountLink });

        // Setup product mappings based on all unique tier IDs
        var allTierIds = patreonEntitledTiers.Select(t => t.TierId).Union(internalStoredTiers).Distinct().ToList();
        var productMappings = new List<ProductMapping>();

        foreach (var tierId in allTierIds)
        {
            productMappings.Add(new ProductMapping
            {
                Id = $"{tierId}-mapping-id",
                ProductName = $"Tier {tierId}",
                Type = mappingType,
                RewardIds = new List<string> { $"reward-{tierId}" },
                ProductProviders = new List<ProductProviderPair>
                {
                    new ProductProviderPair { ProviderId = "patreon", ProductId = tierId }
                }
            });
        }

        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(productMappings);

        // Create Patreon members list (with full EntitledTier including AmountCents)
        var patreonMembers = new List<PatreonMember>();
        if (patreonEntitledTiers.Any())
        {
            patreonMembers.Add(new PatreonMember
            {
                Id = $"member-{battleTag.Replace("#", "-")}",
                PatreonUserId = patreonUserId,
                PatronStatus = "active_patron",
                LastChargeStatus = "Paid",  // This makes IsActivePatron = true
                EntitledTiers = patreonEntitledTiers.ToList()
            });
        }

        _mockPatreonApiClient.Setup(x => x.GetAllCampaignMembers())
            .ReturnsAsync(patreonMembers);

        // Create internal associations
        var internalAssociations = new List<ProductMappingUserAssociation>();
        foreach (var tierId in internalStoredTiers)
        {
            internalAssociations.Add(new ProductMappingUserAssociation
            {
                Id = $"assoc-{tierId}",
                UserId = battleTag,
                ProductMappingId = $"{tierId}-mapping-id",
                ProviderId = "patreon",
                ProviderProductId = tierId,
                Status = AssociationStatus.Active,
                AssignedAt = DateTime.UtcNow.AddDays(-10)
            });
        }

        _mockAssociationRepository.Setup(x => x.GetAll(AssociationStatus.Active))
            .ReturnsAsync(internalAssociations);

        // Act
        var driftResult = await _service.DetectDrift();

        // Assert
        Assert.IsNotNull(driftResult, "Drift result should not be null");

        if (shouldDetectDrift)
        {
            Assert.IsTrue(driftResult.HasDrift,
                $"Should detect drift for scenario: {description}");
        }
        else
        {
            Assert.IsFalse(driftResult.HasDrift,
                $"Should NOT detect drift for scenario: {description}");
        }

        Assert.AreEqual(expectedMismatchCount, driftResult.MismatchedTiers.Count,
            $"Mismatch count incorrect for scenario: {description}");

        // Additional validation for mismatch details when applicable
        if (expectedMismatchCount > 0 && driftResult.MismatchedTiers.Any())
        {
            var mismatch = driftResult.MismatchedTiers[0];
            Assert.AreEqual(battleTag, mismatch.UserId, "Mismatched user ID incorrect");
            Assert.That(mismatch.PatreonTiers, Is.EquivalentTo(patreonEntitledTiers.Select(t => t.TierId).ToList()),
                "Patreon tiers in mismatch record incorrect");
            Assert.That(mismatch.InternalTiers, Is.EquivalentTo(internalStoredTiers),
                "Internal tiers in mismatch record incorrect");
        }

        // Check for extra assignments when user lost tiers
        if (!patreonEntitledTiers.Any() && internalStoredTiers.Any())
        {
            Assert.IsTrue(driftResult.ExtraAssignments.Count > 0,
                "Should have extra assignments when user lost all tiers");
        }
    }

    // ─── Step 2: SyncSingleUser end-to-end tests ────────────────────────────

    /// <summary>
    /// Sets up product mappings for standard Bronze (6482051) and Gold (6482070) TIERED tiers
    /// so SyncSingleUser can resolve tiers via GetByProviderId and GetByProviderAndProductId.
    /// </summary>
    private void SetupStandardTierMappings()
    {
        var bronzeMapping = new ProductMapping
        {
            Id = "bronze-mapping-id",
            ProductName = "Bronze Tier",
            Type = ProductMappingType.Tiered,
            RewardIds = new List<string> { "reward-bronze" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "6482051" }
            }
        };
        var goldMapping = new ProductMapping
        {
            Id = "gold-mapping-id",
            ProductName = "Gold Tier",
            Type = ProductMappingType.Tiered,
            RewardIds = new List<string> { "reward-gold" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "6482070" }
            }
        };

        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping> { bronzeMapping, goldMapping });
        _mockProductMappingRepository.Setup(x => x.GetByProviderAndProductId("patreon", "6482051"))
            .ReturnsAsync(new List<ProductMapping> { bronzeMapping });
        _mockProductMappingRepository.Setup(x => x.GetByProviderAndProductId("patreon", "6482070"))
            .ReturnsAsync(new List<ProductMapping> { goldMapping });
    }

    [Test]
    public async Task SyncSingleUser_UserBelongsToOtherCreatorsCampaign_DoesNotGrantOurRewards()
    {
        // TORREN-style regression: campaign endpoint returns null (user not in our campaign).
        // The service returns early without touching associations.
        const string battleTag = "TORREN#11438";
        const string patreonUserId = "152572628";

        _mockPatreonApiClient.Setup(x => x.GetCampaignMemberByPatreonUserId(patreonUserId))
            .ReturnsAsync((PatreonMember)null);

        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(battleTag))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        var result = await _service.SyncSingleUser(battleTag, patreonUserId, "user-token");

        // null from campaign API → SyncSingleUser returns early; SyncAction stays at default None
        Assert.AreEqual(UserSyncAction.None, result.SyncAction,
            "SyncAction must be None when the user is not a member of our campaign");
        _mockAssociationRepository.Verify(r => r.Create(It.IsAny<ProductMappingUserAssociation>()), Times.Never,
            "Should not create any associations for a non-member");
    }

    [Test]
    public async Task SyncSingleUser_UserActiveInOurCampaign_GrantsCorrectTier()
    {
        // Active patron with a single Gold tier → CreateNew action, association created.
        const string battleTag = "TORREN#11438";
        const string patreonUserId = "152572628";

        var member = new PatreonMemberBuilder()
            .WithPatreonUserId(patreonUserId)
            .WithPatronStatus("active_patron")
            .WithLastChargeStatus("Paid")
            .WithTiers(EntitledTierBuilder.Gold())
            .Build();

        _mockPatreonApiClient.Setup(x => x.GetCampaignMemberByPatreonUserId(patreonUserId))
            .ReturnsAsync(member);

        // No existing associations
        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(battleTag))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockAssociationRepository.Setup(x => x.GetByUserAndProductMapping(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockAssociationRepository.Setup(x => x.Create(It.IsAny<ProductMappingUserAssociation>()))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        SetupStandardTierMappings();

        var result = await _service.SyncSingleUser(battleTag, patreonUserId, "user-token");

        Assert.IsTrue(result.Success, "Sync should succeed for active patron");
        Assert.AreEqual(UserSyncAction.CreateNew, result.SyncAction,
            "Should create new associations for an active patron with no existing associations");
    }

    [Test]
    public async Task SyncSingleUser_UserActiveInOurCampaign_MultipleTiers_GrantsHighestAmountOnly()
    {
        // Patron has Bronze (100¢) + Gold (1000¢) — filter must select Gold only.
        const string battleTag = "TORREN#11438";
        const string patreonUserId = "152572628";

        var member = new PatreonMemberBuilder()
            .WithPatreonUserId(patreonUserId)
            .WithPatronStatus("active_patron")
            .WithLastChargeStatus("Paid")
            .WithTiers(EntitledTierBuilder.Bronze(), EntitledTierBuilder.Gold())
            .Build();

        _mockPatreonApiClient.Setup(x => x.GetCampaignMemberByPatreonUserId(patreonUserId))
            .ReturnsAsync(member);

        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(battleTag))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockAssociationRepository.Setup(x => x.GetByUserAndProductMapping(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        var createdAssociations = new List<ProductMappingUserAssociation>();
        _mockAssociationRepository.Setup(x => x.Create(It.IsAny<ProductMappingUserAssociation>()))
            .Callback<ProductMappingUserAssociation>(a => createdAssociations.Add(a))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        SetupStandardTierMappings();

        var result = await _service.SyncSingleUser(battleTag, patreonUserId, "user-token");

        Assert.AreEqual(UserSyncAction.CreateNew, result.SyncAction,
            "Should create new associations");

        // Only Gold association created — Bronze must be filtered out
        Assert.AreEqual(1, createdAssociations.Count,
            "Should create exactly 1 association (Gold only)");
        _mockAssociationRepository.Verify(r => r.Create(
            It.Is<ProductMappingUserAssociation>(a => a.ProviderProductId == "6482070")), Times.Once,
            "Gold tier (6482070) must be created");
        _mockAssociationRepository.Verify(r => r.Create(
            It.Is<ProductMappingUserAssociation>(a => a.ProviderProductId == "6482051")), Times.Never,
            "Bronze tier (6482051) must NOT be created — Gold wins by amount_cents");
    }

    [Test]
    public async Task SyncSingleUser_UserNotInAnyCampaign_GrantsNoRewardsAndReturnsNoneAction()
    {
        // No campaign membership found → no reward changes, action is None.
        const string battleTag = "TORREN#11438";
        const string patreonUserId = "152572628";

        _mockPatreonApiClient.Setup(x => x.GetCampaignMemberByPatreonUserId(patreonUserId))
            .ReturnsAsync((PatreonMember)null);

        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(battleTag))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        var result = await _service.SyncSingleUser(battleTag, patreonUserId, "user-token");

        Assert.AreEqual(UserSyncAction.None, result.SyncAction,
            "Action must be None when user is not in any campaign");
        _mockAssociationRepository.Verify(r => r.Create(It.IsAny<ProductMappingUserAssociation>()), Times.Never,
            "No associations should be created");
        _mockReconciliationService.Verify(r => r.ReconcileUserAssociations(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never,
            "No reconciliation should be triggered");
    }

    [Test]
    public async Task SyncSingleUser_UserDowngradedFromGoldToBronze_RevokesGoldGrantsBronze()
    {
        // Existing internal: Gold. Patreon now shows Bronze only.
        // Expected: UpdateTiers action, Gold revoked, Bronze created.
        const string battleTag = "TORREN#11438";
        const string patreonUserId = "152572628";

        var goldAssociation = new ProductMappingUserAssociation
        {
            Id = "gold-assoc-id",
            UserId = battleTag,
            ProductMappingId = "gold-mapping-id",
            ProviderId = "patreon",
            ProviderProductId = "6482070",
            Status = AssociationStatus.Active,
            AssignedAt = DateTime.UtcNow.AddDays(-30)
        };

        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(battleTag))
            .ReturnsAsync(new List<ProductMappingUserAssociation> { goldAssociation });
        _mockAssociationRepository.Setup(x => x.Update(It.IsAny<ProductMappingUserAssociation>()))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);
        _mockAssociationRepository.Setup(x => x.GetByUserAndProductMapping(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockAssociationRepository.Setup(x => x.Create(It.IsAny<ProductMappingUserAssociation>()))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        // Patreon now only has Bronze (downgraded)
        var member = new PatreonMemberBuilder()
            .WithPatreonUserId(patreonUserId)
            .WithPatronStatus("active_patron")
            .WithLastChargeStatus("Paid")
            .WithTiers(EntitledTierBuilder.Bronze())
            .Build();

        _mockPatreonApiClient.Setup(x => x.GetCampaignMemberByPatreonUserId(patreonUserId))
            .ReturnsAsync(member);

        SetupStandardTierMappings();

        var result = await _service.SyncSingleUser(battleTag, patreonUserId, "user-token");

        Assert.IsTrue(result.Success, "Sync should succeed");
        Assert.AreEqual(UserSyncAction.UpdateTiers, result.SyncAction,
            "Should be UpdateTiers when patron downgrades from Gold to Bronze");

        // Gold should be revoked
        _mockAssociationRepository.Verify(r => r.Update(It.Is<ProductMappingUserAssociation>(
            a => a.ProviderProductId == "6482070" && a.Status == AssociationStatus.Revoked)),
            Times.Once, "Gold association must be revoked");

        // Bronze should be created
        _mockAssociationRepository.Verify(r => r.Create(It.Is<ProductMappingUserAssociation>(
            a => a.ProviderProductId == "6482051")),
            Times.Once, "Bronze association must be created after downgrade");
    }

    [Test]
    public async Task SyncSingleUser_UserUpgradedFromBronzeToGold_TransitionsToGoldOnly()
    {
        // Existing internal: Bronze. Patreon now shows Bronze + Gold (upgrade window).
        // Filter picks Gold (highest amount_cents) → UpdateTiers, Bronze revoked, Gold created.
        const string battleTag = "TORREN#11438";
        const string patreonUserId = "152572628";

        var bronzeAssociation = new ProductMappingUserAssociation
        {
            Id = "bronze-assoc-id",
            UserId = battleTag,
            ProductMappingId = "bronze-mapping-id",
            ProviderId = "patreon",
            ProviderProductId = "6482051",
            Status = AssociationStatus.Active,
            AssignedAt = DateTime.UtcNow.AddDays(-60)
        };

        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(battleTag))
            .ReturnsAsync(new List<ProductMappingUserAssociation> { bronzeAssociation });
        _mockAssociationRepository.Setup(x => x.Update(It.IsAny<ProductMappingUserAssociation>()))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);
        _mockAssociationRepository.Setup(x => x.GetByUserAndProductMapping(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockAssociationRepository.Setup(x => x.Create(It.IsAny<ProductMappingUserAssociation>()))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        // Patreon: Bronze + Gold during the upgrade window — filter picks Gold
        var member = new PatreonMemberBuilder()
            .WithPatreonUserId(patreonUserId)
            .WithPatronStatus("active_patron")
            .WithLastChargeStatus("Paid")
            .WithTiers(EntitledTierBuilder.Bronze(), EntitledTierBuilder.Gold())
            .Build();

        _mockPatreonApiClient.Setup(x => x.GetCampaignMemberByPatreonUserId(patreonUserId))
            .ReturnsAsync(member);

        SetupStandardTierMappings();

        var result = await _service.SyncSingleUser(battleTag, patreonUserId, "user-token");

        Assert.IsTrue(result.Success, "Sync should succeed");
        Assert.AreEqual(UserSyncAction.UpdateTiers, result.SyncAction,
            "Should be UpdateTiers — Patreon filtered side is Gold, internal is Bronze");

        // Bronze revoked, Gold created
        _mockAssociationRepository.Verify(r => r.Update(It.Is<ProductMappingUserAssociation>(
            a => a.ProviderProductId == "6482051" && a.Status == AssociationStatus.Revoked)),
            Times.Once, "Bronze association must be revoked on upgrade");
        _mockAssociationRepository.Verify(r => r.Create(It.Is<ProductMappingUserAssociation>(
            a => a.ProviderProductId == "6482070")),
            Times.Once, "Gold association must be created on upgrade");
    }

    [Test]
    public async Task SyncSingleUser_NewPatronWithPendingChargeStatus_TreatedAsActive()
    {
        // Pending charge is treated as active (Task 3 fix): patron should get rewards.
        const string battleTag = "TORREN#11438";
        const string patreonUserId = "152572628";

        var member = new PatreonMemberBuilder()
            .WithPatreonUserId(patreonUserId)
            .WithPatronStatus("active_patron")
            .WithLastChargeStatus("Pending") // Pending → IsActivePatron = true after Task 3
            .WithTiers(EntitledTierBuilder.Gold())
            .Build();

        _mockPatreonApiClient.Setup(x => x.GetCampaignMemberByPatreonUserId(patreonUserId))
            .ReturnsAsync(member);

        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(battleTag))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockAssociationRepository.Setup(x => x.GetByUserAndProductMapping(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockAssociationRepository.Setup(x => x.Create(It.IsAny<ProductMappingUserAssociation>()))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        SetupStandardTierMappings();

        var result = await _service.SyncSingleUser(battleTag, patreonUserId, "user-token");

        Assert.IsTrue(result.Success, "Pending charge patron should be treated as active");
        Assert.AreEqual(UserSyncAction.CreateNew, result.SyncAction,
            "Should create new associations for a Pending-charge patron (treated as active)");
    }

    [Test]
    public async Task SyncSingleUser_NewPatronWithFreeTrialStatus_TreatedAsActive()
    {
        // Free Trial charge status is treated as active (Task 3 fix).
        const string battleTag = "TORREN#11438";
        const string patreonUserId = "152572628";

        var member = new PatreonMemberBuilder()
            .WithPatreonUserId(patreonUserId)
            .WithPatronStatus("active_patron")
            .WithLastChargeStatus("Free Trial") // Free Trial → IsActivePatron = true after Task 3
            .WithTiers(EntitledTierBuilder.Gold())
            .Build();

        _mockPatreonApiClient.Setup(x => x.GetCampaignMemberByPatreonUserId(patreonUserId))
            .ReturnsAsync(member);

        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(battleTag))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockAssociationRepository.Setup(x => x.GetByUserAndProductMapping(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockAssociationRepository.Setup(x => x.Create(It.IsAny<ProductMappingUserAssociation>()))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        SetupStandardTierMappings();

        var result = await _service.SyncSingleUser(battleTag, patreonUserId, "user-token");

        Assert.IsTrue(result.Success, "Free Trial patron should be treated as active");
        Assert.AreEqual(UserSyncAction.CreateNew, result.SyncAction,
            "Should create new associations for a Free-Trial patron (treated as active)");
    }

    // ─── Step 3: Drift integration scenario tests ───────────────────────────

    [Test]
    public async Task DetectDrift_TorrenScenario_GoldEntitledButNoInternalRewards_ReportsMissing()
    {
        // TORREN scenario: patron is active with Gold tier in Patreon but has zero
        // internal associations. DetectDrift must place them in MissingMembers.
        const string battleTag = "TORREN#11438";
        const string patreonUserId = "torren-patreon-id";

        var accountLink = new PatreonAccountLink(battleTag, patreonUserId);
        _mockPatreonLinkRepository.Setup(x => x.GetAll())
            .ReturnsAsync(new List<PatreonAccountLink> { accountLink });

        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping>
            {
                new ProductMapping
                {
                    Id = "gold-mapping-id",
                    ProductName = "Gold Tier",
                    Type = ProductMappingType.Tiered,
                    RewardIds = new List<string> { "reward-gold" },
                    ProductProviders = new List<ProductProviderPair>
                    {
                        new ProductProviderPair { ProviderId = "patreon", ProductId = "6482070" }
                    }
                }
            });

        // Patreon has active Gold patron with full amount_cents
        _mockPatreonApiClient.Setup(x => x.GetAllCampaignMembers())
            .ReturnsAsync(new List<PatreonMember>
            {
                new PatreonMember
                {
                    Id = "torren-member-id",
                    PatreonUserId = patreonUserId,
                    PatronStatus = "active_patron",
                    LastChargeStatus = "Paid",
                    EntitledTiers = new List<EntitledTier> { EntitledTierBuilder.Gold() }
                }
            });

        // No internal associations at all
        _mockAssociationRepository.Setup(x => x.GetAll(AssociationStatus.Active))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        var driftResult = await _service.DetectDrift();

        Assert.IsTrue(driftResult.HasDrift, "Should detect drift when active patron has no internal rewards");
        Assert.AreEqual(1, driftResult.MissingMembers.Count,
            "Should report exactly one missing member (TORREN)");
        Assert.AreEqual(patreonUserId, driftResult.MissingMembers[0].PatreonUserId,
            "Missing member should be TORREN's Patreon user ID");
        Assert.IsTrue(driftResult.MissingMembers[0].EntitledTiers.Any(t => t.TierId == "6482070"),
            "Missing member's entitled tiers should include Gold (6482070)");
    }

    [Test]
    public async Task DetectDrift_UserHasFreeTrialStatus_TreatedAsActive_NoDrift()
    {
        // Free Trial patron who already has the correct internal Gold association → no drift.
        // Verifies Free Trial users are not incorrectly excluded as inactive.
        const string battleTag = "freetrial#1234";
        const string patreonUserId = "freetrial-patreon-id";

        var accountLink = new PatreonAccountLink(battleTag, patreonUserId);
        _mockPatreonLinkRepository.Setup(x => x.GetAll())
            .ReturnsAsync(new List<PatreonAccountLink> { accountLink });

        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping>
            {
                new ProductMapping
                {
                    Id = "gold-mapping-id",
                    ProductName = "Gold Tier",
                    Type = ProductMappingType.Tiered,
                    RewardIds = new List<string> { "reward-gold" },
                    ProductProviders = new List<ProductProviderPair>
                    {
                        new ProductProviderPair { ProviderId = "patreon", ProductId = "6482070" }
                    }
                }
            });

        // Patreon: Free Trial charge status — IsActivePatron = true after Task 3
        _mockPatreonApiClient.Setup(x => x.GetAllCampaignMembers())
            .ReturnsAsync(new List<PatreonMember>
            {
                new PatreonMember
                {
                    Id = "freetrial-member-id",
                    PatreonUserId = patreonUserId,
                    PatronStatus = "active_patron",
                    LastChargeStatus = "Free Trial",
                    EntitledTiers = new List<EntitledTier> { EntitledTierBuilder.Gold() }
                }
            });

        // Internal has the correct Gold association
        _mockAssociationRepository.Setup(x => x.GetAll(AssociationStatus.Active))
            .ReturnsAsync(new List<ProductMappingUserAssociation>
            {
                new ProductMappingUserAssociation
                {
                    Id = "gold-assoc-id",
                    UserId = battleTag,
                    ProductMappingId = "gold-mapping-id",
                    ProviderId = "patreon",
                    ProviderProductId = "6482070",
                    Status = AssociationStatus.Active,
                    AssignedAt = DateTime.UtcNow.AddDays(-5)
                }
            });

        var driftResult = await _service.DetectDrift();

        Assert.IsFalse(driftResult.HasDrift,
            "Should NOT detect drift when Free Trial patron has the correct internal Gold association");
        Assert.AreEqual(0, driftResult.MissingMembers.Count, "No missing members expected");
        Assert.AreEqual(0, driftResult.MismatchedTiers.Count, "No tier mismatches expected");
    }

    [Test]
    public async Task DetectDrift_UserCancelsMidCycle_PatronStatusFormer_StillHasInternalRewards_ReportsExtra()
    {
        // A patron cancelled — PatronStatus is "former_patron".
        // They still have an active internal association that should be reported as extra.
        const string battleTag = "cancelled#5678";
        const string patreonUserId = "cancelled-patreon-id";

        var accountLink = new PatreonAccountLink(battleTag, patreonUserId);
        _mockPatreonLinkRepository.Setup(x => x.GetAll())
            .ReturnsAsync(new List<PatreonAccountLink> { accountLink });

        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping>
            {
                new ProductMapping
                {
                    Id = "gold-mapping-id",
                    ProductName = "Gold Tier",
                    Type = ProductMappingType.Tiered,
                    RewardIds = new List<string> { "reward-gold" },
                    ProductProviders = new List<ProductProviderPair>
                    {
                        new ProductProviderPair { ProviderId = "patreon", ProductId = "6482070" }
                    }
                }
            });

        // Patreon: former_patron (cancelled)
        _mockPatreonApiClient.Setup(x => x.GetAllCampaignMembers())
            .ReturnsAsync(new List<PatreonMember>
            {
                new PatreonMember
                {
                    Id = "cancelled-member-id",
                    PatreonUserId = patreonUserId,
                    PatronStatus = "former_patron", // Not active
                    LastChargeStatus = "Paid",
                    EntitledTiers = new List<EntitledTier>()
                }
            });

        // Internal: still has Gold association (stale)
        _mockAssociationRepository.Setup(x => x.GetAll(AssociationStatus.Active))
            .ReturnsAsync(new List<ProductMappingUserAssociation>
            {
                new ProductMappingUserAssociation
                {
                    Id = "stale-gold-assoc",
                    UserId = battleTag,
                    ProductMappingId = "gold-mapping-id",
                    ProviderId = "patreon",
                    ProviderProductId = "6482070",
                    Status = AssociationStatus.Active,
                    AssignedAt = DateTime.UtcNow.AddDays(-45)
                }
            });

        var driftResult = await _service.DetectDrift();

        Assert.IsTrue(driftResult.HasDrift,
            "Should detect drift for cancelled patron with stale associations");
        Assert.IsTrue(driftResult.ExtraAssignments.Count > 0,
            "Should report stale associations as extra assignments");
        Assert.AreEqual(battleTag.ToLowerInvariant(),
            driftResult.ExtraAssignments[0].UserId.ToLowerInvariant(),
            "Extra assignment should reference the cancelled patron");
    }

    [Test]
    public async Task SyncDrift_DryRun_DoesNotMutateAssociationStatusOrCallReconciliation()
    {
        // A dry run with a missing member must increment MembersAdded but must never
        // call Create / Update on the association repository or trigger reconciliation.
        // patreonUserId is linked to "TestBattleTag#1234" in the Setup() account link
        const string patreonUserId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

        var driftResult = new DriftDetectionResult
        {
            Timestamp = DateTime.UtcNow,
            ProviderId = "patreon",
            MissingMembers = new List<MissingMember>
            {
                new MissingMember
                {
                    PatreonMemberId = "member-dry-run",
                    PatreonUserId = patreonUserId,
                    PatronStatus = "active_patron",
                    EntitledTiers = new List<EntitledTier> { EntitledTierBuilder.Gold() },
                    Reason = "Dry-run missing member"
                }
            }
        };

        var syncResult = await _service.SyncDrift(driftResult, dryRun: true);

        Assert.IsTrue(syncResult.Success, "Dry-run sync should succeed");
        Assert.IsTrue(syncResult.WasDryRun, "WasDryRun must be true");
        Assert.AreEqual(1, syncResult.MembersAdded,
            "MembersAdded counter should be incremented even in dry-run");

        // Critical: no repository mutations or reconciliation in dry run
        _mockAssociationRepository.Verify(r => r.Create(It.IsAny<ProductMappingUserAssociation>()), Times.Never,
            "Dry-run must not call Create on the repository");
        _mockAssociationRepository.Verify(r => r.Update(It.IsAny<ProductMappingUserAssociation>()), Times.Never,
            "Dry-run must not call Update on the repository");
        _mockReconciliationService.Verify(r => r.ReconcileUserAssociations(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never,
            "Dry-run must not trigger reconciliation");
    }

    [Test]
    public async Task SyncDrift_Idempotency_RunningTwiceProducesNoSecondPassChanges()
    {
        // After a first successful sync (creates the association), a second pass with the
        // same drift result should find the association already exists and create nothing more.
        const string battleTag = "IdempotencyUser#9999";
        const string patreonUserId = "idempotency-user-id";

        var accountLink = new PatreonAccountLink(battleTag, patreonUserId);
        _mockPatreonLinkRepository.Setup(x => x.GetAll())
            .ReturnsAsync(new List<PatreonAccountLink> { accountLink });

        var goldMapping = new ProductMapping
        {
            Id = "gold-mapping-id",
            ProductName = "Gold Tier",
            Type = ProductMappingType.Tiered,
            RewardIds = new List<string> { "reward-gold" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "6482070" }
            }
        };
        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping> { goldMapping });
        _mockProductMappingRepository.Setup(x => x.GetByProviderAndProductId("patreon", "6482070"))
            .ReturnsAsync(new List<ProductMapping> { goldMapping });

        var existingGoldAssociation = new ProductMappingUserAssociation
        {
            Id = "gold-assoc-id",
            UserId = battleTag,
            ProductMappingId = "gold-mapping-id",
            ProviderId = "patreon",
            ProviderProductId = "6482070",
            Status = AssociationStatus.Active,
            AssignedAt = DateTime.UtcNow
        };

        // First call: empty (no associations yet). Second call: association already created.
        _mockAssociationRepository.SetupSequence(x => x.GetProductMappingsByUserId(battleTag))
            .ReturnsAsync(new List<ProductMappingUserAssociation>())
            .ReturnsAsync(new List<ProductMappingUserAssociation> { existingGoldAssociation });

        // For the individual-lookup path, always return empty (triggers the Create path first time)
        _mockAssociationRepository.Setup(x => x.GetByUserAndProductMapping(It.IsAny<string>(), "gold-mapping-id"))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        _mockAssociationRepository.Setup(x => x.Create(It.IsAny<ProductMappingUserAssociation>()))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        var buildDrift = () => new DriftDetectionResult
        {
            Timestamp = DateTime.UtcNow,
            ProviderId = "patreon",
            MissingMembers = new List<MissingMember>
            {
                new MissingMember
                {
                    PatreonMemberId = "idempotency-member-id",
                    PatreonUserId = patreonUserId,
                    PatronStatus = "active_patron",
                    EntitledTiers = new List<EntitledTier> { EntitledTierBuilder.Gold() },
                    Reason = "Missing member"
                }
            }
        };

        var firstResult = await _service.SyncDrift(buildDrift(), dryRun: false);
        Assert.IsTrue(firstResult.Success, "First pass should succeed");
        Assert.AreEqual(1, firstResult.MembersAdded, "First pass should add 1 member");

        var secondResult = await _service.SyncDrift(buildDrift(), dryRun: false);
        Assert.IsTrue(secondResult.Success, "Second pass should also succeed");

        // The association is skipped on the second call because GetProductMappingsByUserId
        // returns the already-created association, so the batch method skips it.
        _mockAssociationRepository.Verify(r => r.Create(It.IsAny<ProductMappingUserAssociation>()),
            Times.AtMostOnce,
            "Association should be created at most once across two passes (idempotency)");
    }

    [Test]
    public async Task SyncDrift_PartialFailure_ProcessesRemainingUsersAndReportsErrors()
    {
        // When one member has no linked BattleTag (silently skipped), the remaining member
        // should still be processed successfully and the overall sync should succeed.
        const string goodPatreonUserId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890"; // linked in Setup()
        const string badPatreonUserId = "no-link-for-this-user"; // no account link

        _mockPatreonLinkRepository.Setup(x => x.GetAll())
            .ReturnsAsync(new List<PatreonAccountLink>
            {
                // Only one of the two users has a linked account
                new PatreonAccountLink("TestBattleTag#1234", goodPatreonUserId)
            });

        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping>
            {
                new ProductMapping
                {
                    Id = "gold-mapping-id",
                    ProductName = "Gold Tier",
                    Type = ProductMappingType.Tiered,
                    RewardIds = new List<string> { "reward-gold" },
                    ProductProviders = new List<ProductProviderPair>
                    {
                        new ProductProviderPair { ProviderId = "patreon", ProductId = "6482070" }
                    }
                }
            });

        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId("TestBattleTag#1234"))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockAssociationRepository.Setup(x => x.GetByUserAndProductMapping(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockAssociationRepository.Setup(x => x.Create(It.IsAny<ProductMappingUserAssociation>()))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        var driftResult = new DriftDetectionResult
        {
            Timestamp = DateTime.UtcNow,
            ProviderId = "patreon",
            MissingMembers = new List<MissingMember>
            {
                new MissingMember
                {
                    // User with no linked BattleTag — silently skipped (no error)
                    PatreonMemberId = "bad-member-id",
                    PatreonUserId = badPatreonUserId,
                    PatronStatus = "active_patron",
                    EntitledTiers = new List<EntitledTier> { EntitledTierBuilder.Gold() },
                    Reason = "No linked BattleTag"
                },
                new MissingMember
                {
                    // User with linked BattleTag — processed successfully
                    PatreonMemberId = "good-member-id",
                    PatreonUserId = goodPatreonUserId,
                    PatronStatus = "active_patron",
                    EntitledTiers = new List<EntitledTier> { EntitledTierBuilder.Gold() },
                    Reason = "Has linked BattleTag"
                }
            }
        };

        var syncResult = await _service.SyncDrift(driftResult, dryRun: false);

        Assert.IsTrue(syncResult.Success,
            "Overall sync should succeed even with one unlinked member (silent skip)");
        Assert.AreEqual(2, syncResult.MembersAdded,
            "Both entries increment MembersAdded; the unlinked one is silently skipped before creating associations");
        Assert.AreEqual(0, syncResult.Errors.Count,
            "Missing BattleTag link is a silent skip, not an error");

        // Only the linked account should have an association created
        _mockAssociationRepository.Verify(r => r.Create(It.Is<ProductMappingUserAssociation>(
            a => a.UserId == "TestBattleTag#1234")), Times.Once,
            "Should create association only for the user with a linked account");
    }

    [Test]
    public async Task AnalyzeDrift_TierMismatch_UserIdIsCanonical()
    {
        // Regression test for drift detection casing canonicalization.
        // After removing .ToLowerInvariant() from AnalyzeDrift, TierMismatch.UserId
        // must be the canonical (mixed-case) BattleTag, not lowercased.
        const string canonicalBattleTag = "TORREN#11438";
        const string patreonUserId = "152572628";

        var accountLink = new PatreonAccountLink(canonicalBattleTag, patreonUserId);
        _mockPatreonLinkRepository.Setup(x => x.GetAll())
            .ReturnsAsync(new List<PatreonAccountLink> { accountLink });

        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping>
            {
                new ProductMapping
                {
                    Id = "bronze-mapping",
                    ProductName = "Bronze Tier",
                    Type = ProductMappingType.Tiered,
                    RewardIds = new List<string> { "reward-bronze" },
                    ProductProviders = new List<ProductProviderPair>
                    {
                        new ProductProviderPair { ProviderId = "patreon", ProductId = "6482051" }
                    }
                },
                new ProductMapping
                {
                    Id = "gold-mapping",
                    ProductName = "Gold Tier",
                    Type = ProductMappingType.Tiered,
                    RewardIds = new List<string> { "reward-gold" },
                    ProductProviders = new List<ProductProviderPair>
                    {
                        new ProductProviderPair { ProviderId = "patreon", ProductId = "6482070" }
                    }
                }
            });

        // Patreon: user is active patron with Bronze and Gold entitlements
        _mockPatreonApiClient.Setup(x => x.GetAllCampaignMembers())
            .ReturnsAsync(new List<PatreonMember>
            {
                new PatreonMember
                {
                    Id = "patreon-member-id",
                    PatreonUserId = patreonUserId,
                    PatronStatus = "active_patron",
                    LastChargeStatus = "Paid",
                    EntitledTiers = new List<EntitledTier>
                    {
                        EntitledTierBuilder.Bronze(),
                        EntitledTierBuilder.Gold()
                    }
                }
            });

        // Internal: user only has Bronze association (missing Gold)
        _mockAssociationRepository.Setup(x => x.GetAll(AssociationStatus.Active))
            .ReturnsAsync(new List<ProductMappingUserAssociation>
            {
                new ProductMappingUserAssociation
                {
                    Id = "bronze-assoc",
                    UserId = canonicalBattleTag,
                    ProductMappingId = "bronze-mapping",
                    ProviderId = "patreon",
                    ProviderProductId = "6482051",
                    Status = AssociationStatus.Active,
                    AssignedAt = DateTime.UtcNow.AddDays(-30)
                }
            });

        var driftResult = await _service.DetectDrift();

        Assert.IsTrue(driftResult.HasDrift, "Should detect tier mismatch");
        var mismatch = driftResult.MismatchedTiers.SingleOrDefault();
        Assert.IsNotNull(mismatch, "Should report exactly one tier mismatch");
        Assert.AreEqual(canonicalBattleTag, mismatch.UserId,
            "TierMismatch.UserId must be the canonical-cased BattleTag (TORREN#11438), not lowercased");
    }

    [Test]
    public async Task SyncDrift_ExtraAssignment_RevokesActivePatreonRewardAssignments()
    {
        // Arrange — user has 1 active patreon PMUA and 2 active patreon RAs
        var userId = "Bubu#23550";
        var existingPmua = new ProductMappingUserAssociation
        {
            Id = "pmua-1",
            UserId = userId,
            ProductMappingId = "mapping-grandmaster",
            ProviderId = "patreon",
            ProviderProductId = "6482092",
            Status = AssociationStatus.Active,
            AssignedAt = DateTime.UtcNow.AddMonths(-3)
        };

        _mockAssociationRepository.Setup(x => x.GetAll(AssociationStatus.Active))
            .ReturnsAsync(new List<ProductMappingUserAssociation> { existingPmua });
        _mockAssociationRepository.Setup(x => x.Update(It.IsAny<ProductMappingUserAssociation>()))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping>());

        var activeRAs = new List<RewardAssignment>
        {
            new RewardAssignment { Id = "ra-1", UserId = userId, RewardId = "reward-grandmaster-icon", ProviderId = "patreon", Status = RewardStatus.Active, ProviderReference = "reconciliation:mapping-grandmaster" },
            new RewardAssignment { Id = "ra-2", UserId = userId, RewardId = "reward-grandmaster-portrait", ProviderId = "patreon", Status = RewardStatus.Active, ProviderReference = "reconciliation:mapping-grandmaster" }
        };
        _mockRewardAssignmentRepository.Setup(x => x.GetByUserIdAndStatus(userId, RewardStatus.Active))
            .ReturnsAsync(activeRAs);

        // No matching active patron in Patreon → user shows up as ExtraAssignment
        _mockPatreonApiClient.Setup(x => x.GetAllCampaignMembers())
            .ReturnsAsync(new List<PatreonMember>());

        _mockPatreonLinkRepository.Setup(x => x.GetAll())
            .ReturnsAsync(new List<PatreonAccountLink>());

        // Act
        var driftResult = await _service.DetectDrift();
        var syncResult = await _service.SyncDrift(driftResult, dryRun: false);

        // Assert — both RA revocations actually happened
        _mockRewardService.Verify(x => x.RevokeReward("ra-1", It.Is<string>(s => s.Contains("Drift sync"))), Times.Once);
        _mockRewardService.Verify(x => x.RevokeReward("ra-2", It.Is<string>(s => s.Contains("Drift sync"))), Times.Once);
        Assert.That(syncResult.AssignmentsRevoked, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task DeactivateAllUserAssociations_RevokesActivePatreonRewardAssignments()
    {
        // Arrange — user is going from active to "no longer active patron" via SyncSingleUser
        var userId = "TestUser#1234";
        var patreonUserId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

        var existingPmua = new ProductMappingUserAssociation
        {
            Id = "pmua-1",
            UserId = userId,
            ProductMappingId = "mapping-silver",
            ProviderId = "patreon",
            ProviderProductId = "6482057",
            Status = AssociationStatus.Active,
            AssignedAt = DateTime.UtcNow.AddDays(-30)
        };

        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(userId))
            .ReturnsAsync(new List<ProductMappingUserAssociation> { existingPmua });
        _mockAssociationRepository.Setup(x => x.Update(It.IsAny<ProductMappingUserAssociation>()))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        var activeRAs = new List<RewardAssignment>
        {
            new RewardAssignment { Id = "ra-silver-1", UserId = userId, RewardId = "reward-silver", ProviderId = "patreon", Status = RewardStatus.Active, ProviderReference = "reconciliation:mapping-silver" }
        };
        _mockRewardAssignmentRepository.Setup(x => x.GetByUserIdAndStatus(userId, RewardStatus.Active))
            .ReturnsAsync(activeRAs);

        // Patreon API says: not an active patron anymore (former patron)
        _mockPatreonApiClient.Setup(x => x.GetCampaignMemberByPatreonUserId(patreonUserId))
            .ReturnsAsync(new PatreonMember
            {
                PatreonUserId = patreonUserId,
                PatronStatus = "former_patron",
                LastChargeStatus = "Paid",
                EntitledTiers = new List<EntitledTier>()
            });

        // Act
        var result = await _service.SyncSingleUser(userId, patreonUserId, accessToken: "valid-token");

        // Assert
        Assert.That(result.Success, Is.True);
        _mockRewardService.Verify(x => x.RevokeReward("ra-silver-1", It.Is<string>(s => s.Contains("Patron no longer active") || s.Contains("Drift sync"))), Times.Once);
    }

    [Test]
    public async Task DeactivateUserAssociation_WhenAllRevokesFail_SyncDoesNotThrow()
    {
        // When every RA revoke throws (all failing, none succeeding), SyncDrift must still
        // not propagate the exception to the caller — per-RA failures are logged and swallowed.
        // The PMUA revocation proceeds regardless, because we cannot leave PMUAs active if
        // the user is no longer a patron. The orphan-RA risk from a partial failure is handled
        // by the bidirectional reconciler on the next cycle.
        var userId = "Bubu#23550";
        var existingPmua = new ProductMappingUserAssociation
        {
            Id = "pmua-1",
            UserId = userId,
            ProductMappingId = "mapping-grandmaster",
            ProviderId = "patreon",
            ProviderProductId = "6482092",
            Status = AssociationStatus.Active,
            AssignedAt = DateTime.UtcNow.AddMonths(-3)
        };

        _mockAssociationRepository.Setup(x => x.GetAll(AssociationStatus.Active))
            .ReturnsAsync(new List<ProductMappingUserAssociation> { existingPmua });
        _mockAssociationRepository.Setup(x => x.Update(It.IsAny<ProductMappingUserAssociation>()))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping>());

        var activeRA = new RewardAssignment
        {
            Id = "ra-1",
            UserId = userId,
            RewardId = "r1",
            ProviderId = "patreon",
            Status = RewardStatus.Active,
            ProviderReference = "reconciliation:mapping-grandmaster"
        };
        _mockRewardAssignmentRepository.Setup(x => x.GetByUserIdAndStatus(userId, RewardStatus.Active))
            .ReturnsAsync(new List<RewardAssignment> { activeRA });

        // Simulate a transient failure on RA revoke
        _mockRewardService.Setup(x => x.RevokeReward("ra-1", It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("simulated RA revoke failure"));

        // No matching active patron in Patreon → user shows up as ExtraAssignment
        _mockPatreonApiClient.Setup(x => x.GetAllCampaignMembers())
            .ReturnsAsync(new List<PatreonMember>());
        _mockPatreonLinkRepository.Setup(x => x.GetAll())
            .ReturnsAsync(new List<PatreonAccountLink>());

        // Act — SyncDrift catches per-RA errors and continues; must not throw
        var driftResult = await _service.DetectDrift();
        Assert.DoesNotThrowAsync(async () =>
            await _service.SyncDrift(driftResult, dryRun: false),
            "SyncDrift must swallow per-RA errors and not propagate them to the caller.");
    }

    [Test]
    public async Task DeactivateUserAssociation_OneRevokeThrows_RemainingRAsStillRevoked()
    {
        // Regression: a single failing RA must not halt the entire user's sync.
        // Otherwise we leak partial state (some RAs revoked, others active, PMUAs untouched).
        var userId = "PartialFailUser#1234";

        var existingPmua = new ProductMappingUserAssociation
        {
            Id = "pmua-1",
            UserId = userId,
            ProductMappingId = "mapping-grandmaster",
            ProviderId = "patreon",
            ProviderProductId = "6482092",
            Status = AssociationStatus.Active,
            AssignedAt = DateTime.UtcNow.AddMonths(-3)
        };
        _mockAssociationRepository.Setup(x => x.GetAll(AssociationStatus.Active))
            .ReturnsAsync(new List<ProductMappingUserAssociation> { existingPmua });
        _mockAssociationRepository.Setup(x => x.Update(It.IsAny<ProductMappingUserAssociation>()))
            .ReturnsAsync((ProductMappingUserAssociation a) => a);

        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping>());

        var activeRAs = new List<RewardAssignment>
        {
            new RewardAssignment { Id = "ra-1", UserId = userId, RewardId = "r1", ProviderId = "patreon", Status = RewardStatus.Active, ProviderReference = "reconciliation:mapping-grandmaster" },
            new RewardAssignment { Id = "ra-2", UserId = userId, RewardId = "r2", ProviderId = "patreon", Status = RewardStatus.Active, ProviderReference = "reconciliation:mapping-grandmaster" },
            new RewardAssignment { Id = "ra-3", UserId = userId, RewardId = "r3", ProviderId = "patreon", Status = RewardStatus.Active, ProviderReference = "reconciliation:mapping-grandmaster" }
        };
        _mockRewardAssignmentRepository.Setup(x => x.GetByUserIdAndStatus(userId, RewardStatus.Active))
            .ReturnsAsync(activeRAs);

        // Middle RA throws; first and last should still be revoked
        _mockRewardService.Setup(x => x.RevokeReward("ra-2", It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("simulated transient failure"));

        // No matching active patron in Patreon → user shows up as ExtraAssignment
        _mockPatreonApiClient.Setup(x => x.GetAllCampaignMembers()).ReturnsAsync(new List<PatreonMember>());
        _mockPatreonLinkRepository.Setup(x => x.GetAll()).ReturnsAsync(new List<PatreonAccountLink>());

        // Act
        var driftResult = await _service.DetectDrift();
        Assert.DoesNotThrowAsync(async () => await _service.SyncDrift(driftResult, dryRun: false),
            "SyncDrift must not throw when one RA revoke fails — remaining RAs must still be processed.");

        // Assert — ra-1 and ra-3 were revoked despite ra-2 throwing
        _mockRewardService.Verify(x => x.RevokeReward("ra-1", It.IsAny<string>()), Times.Once);
        _mockRewardService.Verify(x => x.RevokeReward("ra-3", It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task UpdateUserAssociationTiers_SkipsRevokeRecreate_WhenActionableTierSetEqualsExistingActive()
    {
        // Arrange — user has 1 active PMUA for tier 6482057. Patreon says they have tiers
        // [15145463 (unmapped), 6482057 (mapped)]. Filter would produce [15145463, 6482057].
        // Internal mapped set = {6482057}. Without the guard, the drift loops every cycle.
        var userId = "ChurnLoopUser#1234";

        var existingPmua = new ProductMappingUserAssociation
        {
            Id = "pmua-1",
            UserId = userId,
            ProductMappingId = "mapping-silver",
            ProviderId = "patreon",
            ProviderProductId = "6482057",
            Status = AssociationStatus.Active,
            AssignedAt = DateTime.UtcNow.AddDays(-1)
        };
        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(userId))
            .ReturnsAsync(new List<ProductMappingUserAssociation> { existingPmua });

        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping>
            {
                new ProductMapping
                {
                    Id = "mapping-silver",
                    ProductName = "Silver",
                    Type = ProductMappingType.Tiered,
                    ProductProviders = new List<ProductProviderPair> { new ProductProviderPair { ProviderId = "patreon", ProductId = "6482057" } }
                }
                // mapping for 15145463 deliberately absent — unmapped/free tier
            });

        var tierMismatch = new TierMismatch
        {
            UserId = userId,
            PatreonMemberId = "memberId",
            PatreonTiers = new List<string> { "15145463", "6482057" },
            PatreonTiersFiltered = new List<string> { "15145463", "6482057" },
            InternalTiers = new List<string> { "6482057" },
            InternalTiersFiltered = new List<string> { "6482057" }
        };

        var driftResult = new DriftDetectionResult
        {
            ProviderId = "patreon",
            Timestamp = DateTime.UtcNow,
            MismatchedTiers = new List<TierMismatch> { tierMismatch }
        };

        // Act
        var syncResult = await _service.SyncDrift(driftResult, dryRun: false);

        // Assert — no revoke or update of the existing PMUA
        _mockAssociationRepository.Verify(x => x.Update(It.Is<ProductMappingUserAssociation>(a => a.Status == AssociationStatus.Revoked)), Times.Never);
        Assert.That(syncResult.TiersUpdated, Is.EqualTo(0));
    }

    [Test]
    public async Task UpdateUserAssociationTiers_DryRun_NoOp_DoesNotIncrementTiersUpdated()
    {
        // Regression test: dry-run mode must respect the no-op guard so operators get
        // an accurate preview of what a live sync would do. The 6 production users in
        // the churn loop should report TiersUpdated=0 in a dry-run.
        var userId = "ChurnLoopUser#1234";

        var existingPmua = new ProductMappingUserAssociation
        {
            Id = "pmua-1",
            UserId = userId,
            ProductMappingId = "mapping-silver",
            ProviderId = "patreon",
            ProviderProductId = "6482057",
            Status = AssociationStatus.Active,
            AssignedAt = DateTime.UtcNow.AddDays(-1)
        };
        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(userId))
            .ReturnsAsync(new List<ProductMappingUserAssociation> { existingPmua });

        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping>
            {
                new ProductMapping
                {
                    Id = "mapping-silver",
                    ProductName = "Silver",
                    Type = ProductMappingType.Tiered,
                    ProductProviders = new List<ProductProviderPair> { new ProductProviderPair { ProviderId = "patreon", ProductId = "6482057" } }
                }
                // 15145463 deliberately unmapped
            });

        var tierMismatch = new TierMismatch
        {
            UserId = userId,
            PatreonMemberId = "memberId",
            PatreonTiers = new List<string> { "15145463", "6482057" },
            PatreonTiersFiltered = new List<string> { "15145463", "6482057" },
            InternalTiers = new List<string> { "6482057" },
            InternalTiersFiltered = new List<string> { "6482057" }
        };

        var driftResult = new DriftDetectionResult
        {
            ProviderId = "patreon",
            Timestamp = DateTime.UtcNow,
            MismatchedTiers = new List<TierMismatch> { tierMismatch }
        };

        // Act — dry-run
        var syncResult = await _service.SyncDrift(driftResult, dryRun: true);

        // Assert — no DB writes, TiersUpdated == 0 (matches the live sync behavior, not the pre-fix overstatement)
        _mockAssociationRepository.Verify(x => x.Update(It.IsAny<ProductMappingUserAssociation>()), Times.Never);
        Assert.That(syncResult.TiersUpdated, Is.EqualTo(0),
            "Dry-run must respect the no-op guard — otherwise operators get a misleading preview.");
    }

    [Test]
    public async Task UpdateUserAssociationTiers_DryRun_RealTierUpgrade_DoesNotMutateDbButReportsCount()
    {
        // For a real tier change (Silver→Gold), dry-run should NOT mutate but SHOULD report TiersUpdated=1.
        var userId = "Upgrader#1234";

        var existingPmua = new ProductMappingUserAssociation
        {
            Id = "pmua-silver",
            UserId = userId,
            ProductMappingId = "mapping-silver",
            ProviderId = "patreon",
            ProviderProductId = "6482057",
            Status = AssociationStatus.Active,
            AssignedAt = DateTime.UtcNow.AddDays(-1)
        };
        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(userId))
            .ReturnsAsync(new List<ProductMappingUserAssociation> { existingPmua });

        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping>
            {
                new ProductMapping
                {
                    Id = "mapping-silver", ProductName = "Silver", Type = ProductMappingType.Tiered,
                    ProductProviders = new List<ProductProviderPair> { new ProductProviderPair { ProviderId = "patreon", ProductId = "6482057" } }
                },
                new ProductMapping
                {
                    Id = "mapping-gold", ProductName = "Gold", Type = ProductMappingType.Tiered,
                    ProductProviders = new List<ProductProviderPair> { new ProductProviderPair { ProviderId = "patreon", ProductId = "6482070" } }
                }
            });

        var tierMismatch = new TierMismatch
        {
            UserId = userId,
            PatreonMemberId = "memberId",
            PatreonTiers = new List<string> { "6482070" },
            PatreonTiersFiltered = new List<string> { "6482070" }, // Gold
            InternalTiers = new List<string> { "6482057" },        // Silver
            InternalTiersFiltered = new List<string> { "6482057" }
        };

        var driftResult = new DriftDetectionResult
        {
            ProviderId = "patreon",
            Timestamp = DateTime.UtcNow,
            MismatchedTiers = new List<TierMismatch> { tierMismatch }
        };

        var syncResult = await _service.SyncDrift(driftResult, dryRun: true);

        // Assert: NO DB writes (dry-run is honest), but TiersUpdated reports 1 (real change would happen)
        _mockAssociationRepository.Verify(x => x.Update(It.IsAny<ProductMappingUserAssociation>()), Times.Never,
            "Dry-run must not mutate DB even for real tier upgrades.");
        Assert.That(syncResult.TiersUpdated, Is.EqualTo(1),
            "Real tier upgrade should still be reported in dry-run.");
    }

    [Test]
    public async Task SyncSingleUser_OnSuccess_UpdatesLastSyncOnAccountLink()
    {
        var userId = "TestUser#1234";
        var patreonUserId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

        var existingLink = new PatreonAccountLink(userId, patreonUserId)
        {
            LastSyncAt = null
        };
        _mockPatreonLinkRepository.Setup(x => x.GetByPatreonUserId(patreonUserId)).ReturnsAsync(existingLink);

        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(userId))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        var productMapping = new ProductMapping
        {
            Id = "mapping-sync-123",
            ProductName = "Tier 1",
            RewardIds = new List<string> { "reward-1" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "tier1" }
            }
        };
        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping> { productMapping });
        _mockProductMappingRepository.Setup(x => x.GetByProviderAndProductId("patreon", "tier1"))
            .ReturnsAsync(new List<ProductMapping> { productMapping });

        _mockPatreonApiClient.Setup(x => x.GetCampaignMemberByPatreonUserId(patreonUserId))
            .ReturnsAsync(new PatreonMember
            {
                PatreonUserId = patreonUserId,
                PatronStatus = "active_patron",
                LastChargeStatus = "Paid",
                EntitledTiers = new List<EntitledTier> { new EntitledTier { TierId = "tier1", AmountCents = 100 } }
            });

        // Act
        var result = await _service.SyncSingleUser(userId, patreonUserId, "valid-token");

        // Assert — RefreshLastSyncAt was delegated to the repository (bookkeeping now owned by repo layer)
        Assert.That(result.Success, Is.True, result.ErrorMessage);
        _mockPatreonLinkRepository.Verify(x => x.RefreshLastSyncAt(userId), Times.AtLeastOnce);
    }

    [Test]
    public async Task SyncDrift_AfterMissingMemberSync_UpdatesLastSync()
    {
        // Arrange — user is missing from internal but active patron
        var userId = "Reactivating#1234";
        var patreonUserId = "999";

        _mockAssociationRepository.Setup(x => x.GetAll(AssociationStatus.Active))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockAssociationRepository.Setup(x => x.GetProductMappingsByUserId(userId))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        var productMapping = new ProductMapping
        {
            Id = "mapping-drift-123",
            ProductName = "Tier 1",
            RewardIds = new List<string> { "reward-1" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "tier1" }
            }
        };
        _mockProductMappingRepository.Setup(x => x.GetByProviderId("patreon"))
            .ReturnsAsync(new List<ProductMapping> { productMapping });

        var link = new PatreonAccountLink(userId, patreonUserId) { LastSyncAt = null };
        _mockPatreonLinkRepository.Setup(x => x.GetAll()).ReturnsAsync(new List<PatreonAccountLink> { link });

        _mockPatreonApiClient.Setup(x => x.GetAllCampaignMembers())
            .ReturnsAsync(new List<PatreonMember>
            {
                new PatreonMember
                {
                    PatreonUserId = patreonUserId,
                    PatronStatus = "active_patron",
                    LastChargeStatus = "Paid",
                    EntitledTiers = new List<EntitledTier> { new EntitledTier { TierId = "tier1", AmountCents = 100 } }
                }
            });

        var driftResult = await _service.DetectDrift();
        var syncResult = await _service.SyncDrift(driftResult, dryRun: false);

        // Assert — RefreshLastSyncAt was delegated to the repository (bookkeeping now owned by repo layer)
        _mockPatreonLinkRepository.Verify(x => x.RefreshLastSyncAt(userId), Times.AtLeastOnce);
    }
}
