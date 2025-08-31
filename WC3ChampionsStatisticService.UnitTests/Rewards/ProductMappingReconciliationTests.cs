using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Common.Repositories;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
using W3ChampionsStatisticService.Rewards.Services;

namespace WC3ChampionsStatisticService.Tests.Rewards;

[TestFixture]
public class ProductMappingReconciliationTests
{
    private Mock<IProductMappingService> _mockProductMappingService;
    private Mock<IProductMappingUserAssociationRepository> _mockAssociationRepo;
    private Mock<IRewardAssignmentRepository> _mockAssignmentRepo;
    private Mock<IRewardRepository> _mockRewardRepo;
    private Mock<IRewardService> _mockRewardService;
    private Mock<ILogger<ProductMappingReconciliationService>> _mockLogger;
    private ProductMappingReconciliationService _service;

    private ProductMapping _testMapping;
    private List<ProductMappingUserAssociation> _testAssociations;
    private List<RewardAssignment> _testAssignments;

    [SetUp]
    public void Setup()
    {
        _mockProductMappingService = new Mock<IProductMappingService>();
        _mockAssociationRepo = new Mock<IProductMappingUserAssociationRepository>();
        _mockAssignmentRepo = new Mock<IRewardAssignmentRepository>();
        _mockRewardRepo = new Mock<IRewardRepository>();
        _mockRewardService = new Mock<IRewardService>();
        _mockLogger = new Mock<ILogger<ProductMappingReconciliationService>>();

        _service = new ProductMappingReconciliationService(
            _mockProductMappingService.Object,
            _mockRewardService.Object,
            _mockLogger.Object);

        // Setup default reward mocks
        _mockRewardRepo.Setup(x => x.GetById(It.IsAny<string>()))
            .ReturnsAsync((string id) => new Reward
            {
                Id = id,
                DisplayId = $"test_reward_{id}",
                IsActive = true,
                Duration = RewardDuration.Permanent()
            });

        // Setup assignment repository Create method
        _mockAssignmentRepo.Setup(x => x.Create(It.IsAny<RewardAssignment>()))
            .ReturnsAsync((RewardAssignment assignment) => assignment);

        // Setup GetUserAssociation for the service method
        _mockProductMappingService.Setup(x => x.GetUserAssociation(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string userId, string mappingId) =>
            {
                return _testAssociations.FirstOrDefault(a => a.UserId == userId && a.ProductMappingId == mappingId);
            });

        SetupTestData();
    }

    private void SetupTestData()
    {
        _testMapping = new ProductMapping
        {
            Id = "mapping-123",
            ProductName = "Test Tier",
            RewardIds = new List<string> { "reward-a", "reward-b" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = "patreon", ProductId = "tier-123" }
            }
        };

        _testAssociations = new List<ProductMappingUserAssociation>
        {
            new ProductMappingUserAssociation
            {
                Id = "assoc-1",
                UserId = "TestUser#1234",
                ProductMappingId = "mapping-123",
                ProviderId = "patreon",
                ProviderProductId = "tier-123",
                Status = AssociationStatus.Active,
                AssignedAt = DateTime.UtcNow.AddDays(-5),
                ExpiresAt = DateTime.UtcNow.AddDays(25)
            }
        };

        _testAssignments = new List<RewardAssignment>
        {
            new RewardAssignment
            {
                Id = "assignment-1",
                UserId = "TestUser#1234",
                RewardId = "reward-a",
                ProviderId = "patreon",
                Status = RewardStatus.Active,
                AssignedAt = DateTime.UtcNow.AddDays(-5),
                Metadata = new Dictionary<string, object>
                {
                    ["product_mapping_id"] = "mapping-123"
                }
            }
        };
    }

    [Test]
    public async Task PreviewReconciliation_ReturnsCorrectPlan_WhenUserNeedsNewReward()
    {
        // Arrange
        _mockProductMappingService.Setup(x => x.GetProductMappingById("mapping-123"))
            .ReturnsAsync(_testMapping);

        _mockProductMappingService.Setup(x => x.GetAssociationsByProductMappingId("mapping-123"))
            .ReturnsAsync(_testAssociations);

        _mockRewardService.Setup(x => x.GetUserRewards("TestUser#1234"))
            .ReturnsAsync(_testAssignments);

        // Act
        var result = await _service.PreviewReconciliation("mapping-123");

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.WasDryRun, Is.True);
        Assert.That(result.TotalUsersAffected, Is.EqualTo(1));
        Assert.That(result.UserReconciliations, Has.Count.EqualTo(1));

        var userReconciliation = result.UserReconciliations.First();
        Assert.That(userReconciliation.UserId, Is.EqualTo("TestUser#1234"));
        Assert.That(userReconciliation.Actions, Has.Count.EqualTo(1));

        var action = userReconciliation.Actions.First();
        Assert.That(action.Type, Is.EqualTo(ReconciliationActionType.Added));
        Assert.That(action.RewardId, Is.EqualTo("reward-b"));
    }

    [Test]
    public async Task PreviewReconciliation_ReturnsCorrectPlan_WhenUserHasExtraReward()
    {
        // Arrange
        var mappingWithOneReward = new ProductMapping
        {
            Id = "mapping-123",
            ProductName = "Test Tier",
            RewardIds = new List<string> { "reward-a" }, // Only one reward now
            ProductProviders = _testMapping.ProductProviders
        };

        var assignmentsWithExtraReward = new List<RewardAssignment>
        {
            _testAssignments.First(), // reward-a (should keep)
            new RewardAssignment
            {
                Id = "assignment-2",
                UserId = "TestUser#1234",
                RewardId = "reward-c", // Extra reward not in mapping
                ProviderId = "patreon",
                Status = RewardStatus.Active,
                Metadata = new Dictionary<string, object>
                {
                    ["product_mapping_id"] = "mapping-123"
                }
            }
        };

        _mockProductMappingService.Setup(x => x.GetProductMappingById("mapping-123"))
            .ReturnsAsync(mappingWithOneReward);

        _mockProductMappingService.Setup(x => x.GetAssociationsByProductMappingId("mapping-123"))
            .ReturnsAsync(_testAssociations);

        _mockRewardService.Setup(x => x.GetUserRewards("TestUser#1234"))
            .ReturnsAsync(assignmentsWithExtraReward);

        // Act
        var result = await _service.PreviewReconciliation("mapping-123");

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.UserReconciliations, Has.Count.EqualTo(1));

        var userReconciliation = result.UserReconciliations.First();
        Assert.That(userReconciliation.Actions, Has.Count.EqualTo(1));

        var action = userReconciliation.Actions.First();
        Assert.That(action.Type, Is.EqualTo(ReconciliationActionType.Removed));
        Assert.That(action.RewardId, Is.EqualTo("reward-c"));
        Assert.That(action.AssignmentId, Is.EqualTo("assignment-2"));
    }

    [Test]
    public async Task PreviewReconciliation_ReturnsNoActions_WhenUserRewardsMatch()
    {
        // Arrange
        var mappingWithOneReward = new ProductMapping
        {
            Id = "mapping-123",
            ProductName = "Test Tier",
            RewardIds = new List<string> { "reward-a" }, // Only reward-a
            ProductProviders = _testMapping.ProductProviders
        };

        _mockProductMappingService.Setup(x => x.GetProductMappingById("mapping-123"))
            .ReturnsAsync(mappingWithOneReward);

        _mockProductMappingService.Setup(x => x.GetAssociationsByProductMappingId("mapping-123"))
            .ReturnsAsync(_testAssociations);

        _mockRewardService.Setup(x => x.GetUserRewards("TestUser#1234"))
            .ReturnsAsync(_testAssignments); // Only has reward-a

        // Act
        var result = await _service.PreviewReconciliation("mapping-123");

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.UserReconciliations, Has.Count.EqualTo(0));
    }

    [Test]
    public async Task ReconcileProductMapping_ExecutesAdditions_WhenNotDryRun()
    {
        // Arrange
        var oldMapping = new ProductMapping
        {
            Id = "mapping-123",
            ProductName = "Test Tier",
            RewardIds = new List<string> { "reward-a" }
        };

        var newMapping = new ProductMapping
        {
            Id = "mapping-123",
            ProductName = "Test Tier",
            RewardIds = new List<string> { "reward-a", "reward-b" }, // Added reward-b
            ProductProviders = _testMapping.ProductProviders
        };

        _mockProductMappingService.Setup(x => x.GetAssociationsByProductMappingId("mapping-123"))
            .ReturnsAsync(_testAssociations);

        _mockProductMappingService.Setup(x => x.GetUserAssociation("TestUser#1234", "mapping-123"))
            .ReturnsAsync(_testAssociations.First());

        _mockRewardService.Setup(x => x.GetUserRewards("TestUser#1234"))
            .ReturnsAsync(_testAssignments);

        // Act
        var result = await _service.ReconcileProductMapping("mapping-123", oldMapping, newMapping, dryRun: false);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.WasDryRun, Is.False);
        Assert.That(result.RewardsAdded, Is.EqualTo(1));
        Assert.That(result.RewardsRevoked, Is.EqualTo(0));

        // Verify that AssignRewardWithEventId was called
        _mockRewardService.Verify(x => x.AssignRewardWithEventId(
            "TestUser#1234",
            "reward-b",
            "patreon",
            It.Is<string>(s => s.Contains("reconciliation:mapping-123")),
            It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task ReconcileProductMapping_ExecutesRemovals_WhenNotDryRun()
    {
        // Arrange
        var oldMapping = new ProductMapping
        {
            Id = "mapping-123",
            ProductName = "Test Tier",
            RewardIds = new List<string> { "reward-a", "reward-b" }
        };

        var newMapping = new ProductMapping
        {
            Id = "mapping-123",
            ProductName = "Test Tier",
            RewardIds = new List<string> { "reward-a" }, // Removed reward-b
            ProductProviders = _testMapping.ProductProviders
        };

        var assignmentsWithBothRewards = new List<RewardAssignment>
        {
            _testAssignments.First(), // reward-a
            new RewardAssignment
            {
                Id = "assignment-2",
                UserId = "TestUser#1234",
                RewardId = "reward-b",
                ProviderId = "patreon",
                Status = RewardStatus.Active,
                Metadata = new Dictionary<string, object>
                {
                    ["product_mapping_id"] = "mapping-123"
                }
            }
        };

        _mockProductMappingService.Setup(x => x.GetAssociationsByProductMappingId("mapping-123"))
            .ReturnsAsync(_testAssociations);

        _mockProductMappingService.Setup(x => x.GetUserAssociation("TestUser#1234", "mapping-123"))
            .ReturnsAsync(_testAssociations.First());

        _mockRewardService.Setup(x => x.GetUserRewards("TestUser#1234"))
            .ReturnsAsync(assignmentsWithBothRewards);

        // Act
        var result = await _service.ReconcileProductMapping("mapping-123", oldMapping, newMapping, dryRun: false);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.RewardsAdded, Is.EqualTo(0));
        Assert.That(result.RewardsRevoked, Is.EqualTo(1));

        // Verify that RevokeReward was called with correct reason
        _mockRewardService.Verify(x => x.RevokeReward(
            "assignment-2",
            "Product mapping reconciliation: Reward removed from Test Tier"), Times.Once);
    }

    [Test]
    public async Task ReconcileProductMapping_SkipsExecution_WhenDryRun()
    {
        // Arrange
        var oldMapping = new ProductMapping
        {
            Id = "mapping-123",
            ProductName = "Test Tier",
            RewardIds = new List<string> { "reward-a" }
        };

        var newMapping = new ProductMapping
        {
            Id = "mapping-123",
            ProductName = "Test Tier",
            RewardIds = new List<string> { "reward-a", "reward-b" },
            ProductProviders = _testMapping.ProductProviders
        };

        _mockProductMappingService.Setup(x => x.GetAssociationsByProductMappingId("mapping-123"))
            .ReturnsAsync(_testAssociations);

        _mockRewardService.Setup(x => x.GetUserRewards("TestUser#1234"))
            .ReturnsAsync(_testAssignments);

        // Act
        var result = await _service.ReconcileProductMapping("mapping-123", oldMapping, newMapping, dryRun: true);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.WasDryRun, Is.True);
        Assert.That(result.UserReconciliations, Has.Count.EqualTo(1));

        // Verify that no actual reward operations were called
        _mockRewardService.Verify(x => x.AssignRewardWithEventId(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockRewardService.Verify(x => x.RevokeReward(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ReconcileProductMapping_HandlesErrors_GracefullyPerUser()
    {
        // Arrange
        var oldMapping = new ProductMapping { Id = "mapping-123", RewardIds = new List<string> { "reward-a" } };
        var newMapping = new ProductMapping
        {
            Id = "mapping-123",
            ProductName = "Test Tier",
            RewardIds = new List<string> { "reward-a", "reward-b" },
            ProductProviders = _testMapping.ProductProviders
        };

        var multipleAssociations = new List<ProductMappingUserAssociation>
        {
            _testAssociations.First(),
            new ProductMappingUserAssociation
            {
                Id = "assoc-2",
                UserId = "TestUser2#5678",
                ProductMappingId = "mapping-123",
                ProviderId = "patreon",
                ProviderProductId = "tier-123",
                Status = AssociationStatus.Active,
                AssignedAt = DateTime.UtcNow.AddDays(-5),
                ExpiresAt = DateTime.UtcNow.AddDays(25)
            }
        };

        _mockProductMappingService.Setup(x => x.GetAssociationsByProductMappingId("mapping-123"))
            .ReturnsAsync(multipleAssociations);

        // Set up first user to succeed
        _mockProductMappingService.Setup(x => x.GetUserAssociation("TestUser#1234", "mapping-123"))
            .ReturnsAsync(_testAssociations.First());
        _mockRewardService.Setup(x => x.GetUserRewards("TestUser#1234"))
            .ReturnsAsync(_testAssignments);

        // Set up second user to fail (no association found)
        _mockProductMappingService.Setup(x => x.GetUserAssociation("TestUser2#5678", "mapping-123"))
            .ReturnsAsync((ProductMappingUserAssociation)null);
        _mockRewardService.Setup(x => x.GetUserRewards("TestUser2#5678"))
            .ReturnsAsync(new List<RewardAssignment>());

        // Act
        var result = await _service.ReconcileProductMapping("mapping-123", oldMapping, newMapping, dryRun: false);

        // Assert
        Assert.That(result.Success, Is.False); // Should fail due to second user error
        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.That(result.Errors.First(), Does.Contain("TestUser2#5678"));
        Assert.That(result.RewardsAdded, Is.EqualTo(1)); // First user should succeed
    }

    [Test]
    public async Task ReconcileAllMappings_ProcessesMultipleMappings()
    {
        // Arrange
        var mappings = new List<ProductMapping>
        {
            new ProductMapping
            {
                Id = "mapping-1",
                ProductName = "Tier 1",
                RewardIds = new List<string> { "reward-a" },
                ProductProviders = _testMapping.ProductProviders
            },
            new ProductMapping
            {
                Id = "mapping-2",
                ProductName = "Tier 2",
                RewardIds = new List<string> { "reward-b" },
                ProductProviders = _testMapping.ProductProviders
            }
        };

        _mockProductMappingService.Setup(x => x.GetAllProductMappings())
            .ReturnsAsync(mappings);

        // Setup both mappings to have no reconciliation needed
        _mockProductMappingService.Setup(x => x.GetAssociationsByProductMappingId(It.IsAny<string>()))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        // Act
        var result = await _service.ReconcileAllMappings(dryRun: true);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.ProductMappingId, Is.EqualTo("ALL"));
        Assert.That(result.ProductMappingName, Is.EqualTo("All Product Mappings"));
    }

    [Test]
    public void PreviewReconciliation_ThrowsException_WhenMappingNotFound()
    {
        // Arrange
        _mockProductMappingService.Setup(x => x.GetProductMappingById("non-existent"))
            .ReturnsAsync((ProductMapping)null);

        // Act & Assert
        Assert.That(async () => await _service.PreviewReconciliation("non-existent"),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contain("Product mapping non-existent not found"));
    }
}
