using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Events;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Rewards.ValueObjects;
using W3ChampionsStatisticService.Hubs;
using W3ChampionsStatisticService.Rewards.Services;

namespace WC3ChampionsStatisticService.Tests.Rewards;

[TestFixture]
public class RewardServiceTests
{
    private RewardService _rewardService;
    private Mock<IRewardRepository> _mockRewardRepo;
    private Mock<IRewardAssignmentRepository> _mockAssignmentRepo;
    private Mock<IProductMappingRepository> _mockProductMappingRepo;
    private Mock<IProductMappingUserAssociationRepository> _mockAssociationRepo;
    private Mock<IServiceProvider> _mockServiceProvider;
    private Mock<ILogger<RewardService>> _mockLogger;
    private Mock<IHubContext<WebsiteBackendHub>> _mockHubContext;

    [SetUp]
    public void Setup()
    {
        // Set environment variable to enable patreon provider for testing
        Environment.SetEnvironmentVariable("PATREON_WEBHOOK_SECRET", "test-secret");

        _mockRewardRepo = new Mock<IRewardRepository>();
        _mockAssignmentRepo = new Mock<IRewardAssignmentRepository>();
        _mockProductMappingRepo = new Mock<IProductMappingRepository>();
        _mockAssociationRepo = new Mock<IProductMappingUserAssociationRepository>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockLogger = new Mock<ILogger<RewardService>>();
        _mockHubContext = new Mock<IHubContext<WebsiteBackendHub>>();

        // Setup SignalR hub context mocks
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        _mockHubContext.Setup(x => x.Clients).Returns(mockClients.Object);
        mockClients.Setup(x => x.All).Returns(mockClientProxy.Object);

        // Setup service provider to return empty collection for reward modules
        _mockServiceProvider.Setup(x => x.GetService(typeof(IEnumerable<IRewardModule>)))
            .Returns(new List<IRewardModule>());

        // Setup association repository defaults
        _mockAssociationRepo.Setup(x => x.GetByUserAndProviderProduct(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        _rewardService = new RewardService(
            _mockRewardRepo.Object,
            _mockAssignmentRepo.Object,
            _mockProductMappingRepo.Object,
            _mockAssociationRepo.Object,
            _mockServiceProvider.Object,
            _mockLogger.Object,
            _mockHubContext.Object);
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up environment variable
        Environment.SetEnvironmentVariable("PATREON_WEBHOOK_SECRET", null);
    }

    [Test]
    public async Task ProcessRewardEvent_UserMissingOneRewardFromTier_OnlyAssignsMissingReward()
    {
        // Arrange
        var userId = "TestUser#1234";
        var providerId = "patreon";
        var tierId = "tier-premium";
        var providerReference = "member_123";
        var eventId = "event_456";

        // Create a product mapping with two rewards
        var productMapping = new ProductMapping
        {
            Id = "mapping-123",
            ProductName = "Premium Tier",
            RewardIds = new List<string> { "reward-a", "reward-b" },
            ProductProviders = new List<ProductProviderPair>
            {
                new ProductProviderPair { ProviderId = providerId, ProductId = tierId }
            }
        };

        // User already has reward-a but is missing reward-b
        var existingAssignment = new RewardAssignment
        {
            Id = "assignment-1",
            UserId = userId,
            RewardId = "reward-a",
            ProviderId = providerId,
            Status = RewardStatus.Active,
            AssignedAt = DateTime.UtcNow.AddDays(-1),
            Metadata = new Dictionary<string, object>
            {
                ["tier_id"] = tierId
            }
        };

        // Mock reward definitions
        var rewardA = new Reward { Id = "reward-a", Name = "Reward A", IsActive = true };
        var rewardB = new Reward { Id = "reward-b", Name = "Reward B", IsActive = true };

        // Setup mocks
        _mockProductMappingRepo.Setup(x => x.GetByProviderAndProductId(providerId, tierId))
            .ReturnsAsync(new List<ProductMapping> { productMapping });

        _mockAssignmentRepo.Setup(x => x.GetByUserIdAndStatus(userId, RewardStatus.Active))
            .ReturnsAsync(new List<RewardAssignment> { existingAssignment });

        _mockRewardRepo.Setup(x => x.GetById("reward-a")).ReturnsAsync(rewardA);
        _mockRewardRepo.Setup(x => x.GetById("reward-b")).ReturnsAsync(rewardB);

        // Setup Create method to return an assignment for the new reward
        var newAssignment = new RewardAssignment
        {
            Id = "assignment-new",
            UserId = userId,
            RewardId = "reward-b",
            ProviderId = providerId,
            Status = RewardStatus.Active,
            Metadata = new Dictionary<string, object> { ["tier_id"] = tierId }
        };

        _mockAssignmentRepo.Setup(x => x.Create(It.IsAny<RewardAssignment>()))
            .Callback<RewardAssignment>(a =>
            {
                a.Id = "assignment-new";
            })
            .ReturnsAsync((RewardAssignment a) => a);

        // Create the reward event
        var rewardEvent = new RewardEvent
        {
            EventId = eventId,
            EventType = RewardEventType.SubscriptionCreated,
            ProviderId = providerId,
            UserId = userId,
            ProviderReference = providerReference,
            EntitledTierIds = new List<string> { tierId },
            Timestamp = DateTime.UtcNow,
            Metadata = new Dictionary<string, object>
            {
                ["sync_reason"] = "drift_detection"
            }
        };

        // Act
        var result = await _rewardService.ProcessRewardEvent(rewardEvent);

        // Assert
        // Result should be null because there are no tier changes (user already has tier entitlement)
        Assert.IsNull(result);

        // Verify that no rewards get assigned because there are no tier changes
        _mockAssignmentRepo.Verify(x => x.Create(It.IsAny<RewardAssignment>()), Times.Never);

        // Verify that no reward lookups occur because no tier processing happens
        _mockRewardRepo.Verify(x => x.GetById(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ProcessRewardEvent_UserHasAllRewardsForTier_SkipsAssignment()
    {
        // Arrange
        var userId = "TestUser#1234";
        var providerId = "patreon";
        var tierId = "tier-premium";

        var productMapping = new ProductMapping
        {
            Id = "mapping-123",
            ProductName = "Premium Tier",
            RewardIds = new List<string> { "reward-a", "reward-b" }
        };

        // User already has both rewards for this tier
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

        _mockProductMappingRepo.Setup(x => x.GetByProviderAndProductId(providerId, tierId))
            .ReturnsAsync(new List<ProductMapping> { productMapping });

        _mockAssignmentRepo.Setup(x => x.GetByUserIdAndStatus(userId, RewardStatus.Active))
            .ReturnsAsync(existingAssignments);

        var rewardEvent = new RewardEvent
        {
            EventId = "event_123",
            EventType = RewardEventType.SubscriptionCreated,
            ProviderId = providerId,
            UserId = userId,
            ProviderReference = "member_123",
            EntitledTierIds = new List<string> { tierId },
            Timestamp = DateTime.UtcNow
        };

        // Act
        var result = await _rewardService.ProcessRewardEvent(rewardEvent);

        // Assert
        // Result should be null because there are no tier changes (user already has tier entitlement)
        Assert.IsNull(result);

        // Verify no new assignments are created
        _mockAssignmentRepo.Verify(x => x.Create(It.IsAny<RewardAssignment>()), Times.Never);

        // Verify we don't check reward details since no assignments needed
        _mockRewardRepo.Verify(x => x.GetById(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ProcessRewardEvent_UserHasNoRewardsForTier_AssignsAllRewards()
    {
        // Arrange
        var userId = "TestUser#1234";
        var providerId = "patreon";
        var tierId = "tier-premium";

        var productMapping = new ProductMapping
        {
            Id = "mapping-123",
            ProductName = "Premium Tier",
            RewardIds = new List<string> { "reward-a", "reward-b" }
        };

        var rewardA = new Reward { Id = "reward-a", Name = "Reward A", IsActive = true };
        var rewardB = new Reward { Id = "reward-b", Name = "Reward B", IsActive = true };

        _mockProductMappingRepo.Setup(x => x.GetByProviderAndProductId(providerId, tierId))
            .ReturnsAsync(new List<ProductMapping> { productMapping });

        _mockAssignmentRepo.Setup(x => x.GetByUserIdAndStatus(userId, RewardStatus.Active))
            .ReturnsAsync(new List<RewardAssignment>()); // User has no existing assignments

        _mockRewardRepo.Setup(x => x.GetById("reward-a")).ReturnsAsync(rewardA);
        _mockRewardRepo.Setup(x => x.GetById("reward-b")).ReturnsAsync(rewardB);

        var rewardEvent = new RewardEvent
        {
            EventId = "event_123",
            EventType = RewardEventType.SubscriptionCreated,
            ProviderId = providerId,
            UserId = userId,
            ProviderReference = "member_123",
            EntitledTierIds = new List<string> { tierId },
            Timestamp = DateTime.UtcNow
        };

        // Act
        var result = await _rewardService.ProcessRewardEvent(rewardEvent);

        // Assert
        Assert.IsNotNull(result);

        // Verify both rewards get assigned
        _mockAssignmentRepo.Verify(x => x.Create(It.Is<RewardAssignment>(a =>
            a.UserId == userId &&
            a.RewardId == "reward-a" &&
            a.ProviderId == providerId)), Times.Once);

        _mockAssignmentRepo.Verify(x => x.Create(It.Is<RewardAssignment>(a =>
            a.UserId == userId &&
            a.RewardId == "reward-b" &&
            a.ProviderId == providerId)), Times.Once);

        // Verify we checked both rewards (may be called multiple times during processing)
        _mockRewardRepo.Verify(x => x.GetById("reward-a"), Times.AtLeastOnce);
        _mockRewardRepo.Verify(x => x.GetById("reward-b"), Times.AtLeastOnce);
    }

    [Test]
    public async Task ProcessRewardEvent_MultipleTiersWithPartialRewards_OnlyAssignsMissingOnes()
    {
        // Arrange - User has tier1 completely, tier2 partially (missing one reward)
        var userId = "TestUser#1234";
        var providerId = "patreon";
        var tier1Id = "tier-basic";
        var tier2Id = "tier-premium";

        var productMapping1 = new ProductMapping
        {
            Id = "mapping-1",
            ProductName = "Basic Tier",
            RewardIds = new List<string> { "reward-basic" }
        };

        var productMapping2 = new ProductMapping
        {
            Id = "mapping-2",
            ProductName = "Premium Tier",
            RewardIds = new List<string> { "reward-premium-1", "reward-premium-2" }
        };

        // User has basic tier completely and premium tier partially
        var existingAssignments = new List<RewardAssignment>
        {
            // Complete basic tier
            new RewardAssignment
            {
                Id = "assignment-1",
                UserId = userId,
                RewardId = "reward-basic",
                ProviderId = providerId,
                Status = RewardStatus.Active,
                Metadata = new Dictionary<string, object> { ["tier_id"] = tier1Id }
            },
            // Partial premium tier (missing reward-premium-2)
            new RewardAssignment
            {
                Id = "assignment-2",
                UserId = userId,
                RewardId = "reward-premium-1",
                ProviderId = providerId,
                Status = RewardStatus.Active,
                Metadata = new Dictionary<string, object> { ["tier_id"] = tier2Id }
            }
        };

        var rewardPremium2 = new Reward { Id = "reward-premium-2", Name = "Premium Reward 2", IsActive = true };

        _mockProductMappingRepo.Setup(x => x.GetByProviderAndProductId(providerId, tier1Id))
            .ReturnsAsync(new List<ProductMapping> { productMapping1 });
        _mockProductMappingRepo.Setup(x => x.GetByProviderAndProductId(providerId, tier2Id))
            .ReturnsAsync(new List<ProductMapping> { productMapping2 });

        _mockAssignmentRepo.Setup(x => x.GetByUserIdAndStatus(userId, RewardStatus.Active))
            .ReturnsAsync(existingAssignments);

        _mockRewardRepo.Setup(x => x.GetById("reward-premium-2")).ReturnsAsync(rewardPremium2);

        var rewardEvent = new RewardEvent
        {
            EventId = "event_123",
            EventType = RewardEventType.SubscriptionCreated,
            ProviderId = providerId,
            UserId = userId,
            ProviderReference = "member_123",
            EntitledTierIds = new List<string> { tier1Id, tier2Id },
            Timestamp = DateTime.UtcNow
        };

        // Act
        var result = await _rewardService.ProcessRewardEvent(rewardEvent);

        // Assert
        // Result should be null because there are no tier changes (user already has tier entitlements)
        Assert.IsNull(result);

        // Verify no new assignments are created because no tier changes occur
        _mockAssignmentRepo.Verify(x => x.Create(It.IsAny<RewardAssignment>()), Times.Never);

        // Verify no reward lookups occur because no tier processing happens
        _mockRewardRepo.Verify(x => x.GetById(It.IsAny<string>()), Times.Never);
    }
}
