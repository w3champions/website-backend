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

namespace WC3ChampionsStatisticService.Tests.Rewards;

[TestFixture]
public class OrphanRewardServiceTests
{
    private Mock<IRewardAssignmentRepository> _mockAssignmentRepo;
    private Mock<IProductMappingUserAssociationRepository> _mockAssociationRepo;
    private Mock<IPatreonAccountLinkRepository> _mockLinkRepo;
    private Mock<PatreonApiClient> _mockPatreonApi;
    private Mock<IRewardService> _mockRewardService;
    private Mock<ILogger<OrphanRewardService>> _mockLogger;
    private OrphanRewardService _service;

    [SetUp]
    public void Setup()
    {
        _mockAssignmentRepo = new Mock<IRewardAssignmentRepository>();
        _mockAssociationRepo = new Mock<IProductMappingUserAssociationRepository>();
        _mockLinkRepo = new Mock<IPatreonAccountLinkRepository>();
        _mockPatreonApi = new Mock<PatreonApiClient>(Mock.Of<HttpClient>());
        _mockRewardService = new Mock<IRewardService>();
        _mockLogger = new Mock<ILogger<OrphanRewardService>>();

        _service = new OrphanRewardService(
            _mockAssignmentRepo.Object,
            _mockAssociationRepo.Object,
            _mockLinkRepo.Object,
            _mockPatreonApi.Object,
            _mockRewardService.Object,
            _mockLogger.Object);
    }

    [Test]
    public async Task DetectOrphans_FindsUserWithActiveRAButNoActivePMUAAndNotActivePatron()
    {
        var userId = "Bubu#23550";

        _mockAssignmentRepo.Setup(x => x.GetActiveAssignmentsByProvider("patreon"))
            .ReturnsAsync(new List<RewardAssignment>
            {
                new RewardAssignment { Id = "ra-1", UserId = userId, RewardId = "reward-grandmaster", ProviderId = "patreon", Status = RewardStatus.Active, ProviderReference = "reconciliation:mapping-grandmaster", AssignedAt = System.DateTime.UtcNow.AddMonths(-3) }
            });

        _mockAssociationRepo.Setup(x => x.GetAll(AssociationStatus.Active))
            .ReturnsAsync(new List<ProductMappingUserAssociation>()); // no active PMUAs

        _mockLinkRepo.Setup(x => x.GetAll())
            .ReturnsAsync(new List<PatreonAccountLink>()); // no link

        _mockPatreonApi.Setup(x => x.GetAllCampaignMembers())
            .ReturnsAsync(new List<PatreonMember>()); // not an active patron

        var report = await _service.DetectOrphans();

        Assert.That(report.Entries, Has.Count.EqualTo(1));
        Assert.That(report.Entries[0].UserId, Is.EqualTo(userId));
        Assert.That(report.Entries[0].Assignments.Select(a => a.AssignmentId), Is.EquivalentTo(new[] { "ra-1" }));
    }

    [Test]
    public async Task DetectOrphans_DoesNotIncludeUserWithActivePMUA()
    {
        var userId = "Healthy#1234";

        _mockAssignmentRepo.Setup(x => x.GetActiveAssignmentsByProvider("patreon"))
            .ReturnsAsync(new List<RewardAssignment>
            {
                new RewardAssignment { Id = "ra-1", UserId = userId, RewardId = "reward-x", ProviderId = "patreon", Status = RewardStatus.Active }
            });

        _mockAssociationRepo.Setup(x => x.GetAll(AssociationStatus.Active))
            .ReturnsAsync(new List<ProductMappingUserAssociation>
            {
                new ProductMappingUserAssociation { Id = "pmua-1", UserId = userId, ProductMappingId = "mapping-x", ProviderId = "patreon", Status = AssociationStatus.Active }
            });

        _mockLinkRepo.Setup(x => x.GetAll()).ReturnsAsync(new List<PatreonAccountLink>());
        _mockPatreonApi.Setup(x => x.GetAllCampaignMembers()).ReturnsAsync(new List<PatreonMember>());

        var report = await _service.DetectOrphans();

        Assert.That(report.Entries, Is.Empty);
    }

    [Test]
    public async Task DetectOrphans_DoesNotIncludeUserWhoIsActivePatron()
    {
        var userId = "Reactivating#1234";
        var patreonUserId = "999";

        _mockAssignmentRepo.Setup(x => x.GetActiveAssignmentsByProvider("patreon"))
            .ReturnsAsync(new List<RewardAssignment>
            {
                new RewardAssignment { Id = "ra-1", UserId = userId, RewardId = "reward-x", ProviderId = "patreon", Status = RewardStatus.Active }
            });

        _mockAssociationRepo.Setup(x => x.GetAll(AssociationStatus.Active))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        _mockLinkRepo.Setup(x => x.GetAll())
            .ReturnsAsync(new List<PatreonAccountLink> { new PatreonAccountLink(userId, patreonUserId) });

        _mockPatreonApi.Setup(x => x.GetAllCampaignMembers())
            .ReturnsAsync(new List<PatreonMember>
            {
                new PatreonMember
                {
                    PatreonUserId = patreonUserId,
                    PatronStatus = "active_patron",
                    LastChargeStatus = "Paid",
                    EntitledTiers = new List<EntitledTier> { new EntitledTier { TierId = "6482051", AmountCents = 100 } }
                }
            });

        var report = await _service.DetectOrphans();

        Assert.That(report.Entries, Is.Empty);
    }

    [Test]
    public async Task DetectOrphans_DoesNotMisclassifyActivePatron_WhenLinkBattleTagCasingDiffersFromRA()
    {
        // Regression test: PatreonAccountLink.BattleTag and RewardAssignment.UserId may
        // have differed historically (pre-canonicalization). The active-patron exclusion
        // must be case-insensitive so we don't revoke a paying patron's rewards.
        var raUserId = "Player#1234";
        var linkBattleTag = "player#1234"; // lowercase variant from older data
        var patreonUserId = "12345";

        _mockAssignmentRepo.Setup(x => x.GetActiveAssignmentsByProvider("patreon"))
            .ReturnsAsync(new List<RewardAssignment>
            {
                new RewardAssignment { Id = "ra-1", UserId = raUserId, RewardId = "r", ProviderId = "patreon", Status = RewardStatus.Active }
            });

        _mockAssociationRepo.Setup(x => x.GetAll(AssociationStatus.Active))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());

        _mockLinkRepo.Setup(x => x.GetAll())
            .ReturnsAsync(new List<PatreonAccountLink> { new PatreonAccountLink(linkBattleTag, patreonUserId) });

        _mockPatreonApi.Setup(x => x.GetAllCampaignMembers())
            .ReturnsAsync(new List<PatreonMember>
            {
                new PatreonMember
                {
                    PatreonUserId = patreonUserId,
                    PatronStatus = "active_patron",
                    LastChargeStatus = "Paid",
                    EntitledTiers = new List<EntitledTier> { new EntitledTier { TierId = "t1", AmountCents = 100 } }
                }
            });

        var report = await _service.DetectOrphans();

        // Should NOT be classified as orphan — they are an active patron despite casing mismatch
        Assert.That(report.Entries, Is.Empty,
            "Active patron with case-different BattleTag must not be flagged as orphan.");
    }

    [Test]
    public async Task RevokeOrphans_RevokesOnlyApprovedUsers_ThatStillAppearInFreshDetection()
    {
        var bubu = "Bubu#23550";
        var stale = "StaleUser#1234"; // approved by admin but no longer in fresh detection
        var actor = "Faro#2494";

        // Detection setup — only Bubu is currently an orphan
        _mockAssignmentRepo.Setup(x => x.GetActiveAssignmentsByProvider("patreon"))
            .ReturnsAsync(new List<RewardAssignment>
            {
                new RewardAssignment { Id = "ra-bubu-1", UserId = bubu, RewardId = "r1", ProviderId = "patreon", Status = RewardStatus.Active, ProviderReference = "reconciliation:m" }
            });
        _mockAssociationRepo.Setup(x => x.GetAll(AssociationStatus.Active))
            .ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockLinkRepo.Setup(x => x.GetAll()).ReturnsAsync(new List<PatreonAccountLink>());
        _mockPatreonApi.Setup(x => x.GetAllCampaignMembers()).ReturnsAsync(new List<PatreonMember>());

        var approved = new HashSet<string> { bubu, stale };

        var result = await _service.RevokeOrphans(approved, actor);

        _mockRewardService.Verify(x => x.RevokeReward("ra-bubu-1", It.Is<string>(s => s.Contains(actor))), Times.Once);
        Assert.That(result.UsersTouched, Is.EqualTo(1));
        Assert.That(result.AssignmentsRevoked, Is.EqualTo(1));
        Assert.That(result.SkippedUserIdsNotInLatestDetection, Is.EquivalentTo(new[] { stale }));
    }

    [Test]
    public async Task RevokeOrphans_AllAssignmentsForUserFail_DoesNotIncrementUsersTouched()
    {
        var bubu = "Bubu#23550";
        var actor = "Faro#2494";

        _mockAssignmentRepo.Setup(x => x.GetActiveAssignmentsByProvider("patreon"))
            .ReturnsAsync(new List<RewardAssignment>
            {
                new RewardAssignment { Id = "ra-1", UserId = bubu, RewardId = "r1", ProviderId = "patreon", Status = RewardStatus.Active, ProviderReference = "reconciliation:m" },
                new RewardAssignment { Id = "ra-2", UserId = bubu, RewardId = "r2", ProviderId = "patreon", Status = RewardStatus.Active, ProviderReference = "reconciliation:m" }
            });
        _mockAssociationRepo.Setup(x => x.GetAll(AssociationStatus.Active)).ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockLinkRepo.Setup(x => x.GetAll()).ReturnsAsync(new List<PatreonAccountLink>());
        _mockPatreonApi.Setup(x => x.GetAllCampaignMembers()).ReturnsAsync(new List<PatreonMember>());

        // Every revoke throws
        _mockRewardService.Setup(x => x.RevokeReward(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new System.InvalidOperationException("simulated"));

        var result = await _service.RevokeOrphans(new HashSet<string> { bubu }, actor);

        Assert.That(result.AssignmentsRevoked, Is.EqualTo(0), "No assignments succeeded.");
        Assert.That(result.UsersTouched, Is.EqualTo(0), "User should not be counted as touched if 0 assignments succeeded.");
        Assert.That(result.Errors, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task RevokeOrphans_LogsAndContinues_OnRevokeFailure()
    {
        var bubu = "Bubu#23550";
        var actor = "Faro#2494";

        _mockAssignmentRepo.Setup(x => x.GetActiveAssignmentsByProvider("patreon"))
            .ReturnsAsync(new List<RewardAssignment>
            {
                new RewardAssignment { Id = "ra-1", UserId = bubu, RewardId = "r1", ProviderId = "patreon", Status = RewardStatus.Active, ProviderReference = "reconciliation:m" },
                new RewardAssignment { Id = "ra-2", UserId = bubu, RewardId = "r2", ProviderId = "patreon", Status = RewardStatus.Active, ProviderReference = "reconciliation:m" }
            });
        _mockAssociationRepo.Setup(x => x.GetAll(AssociationStatus.Active)).ReturnsAsync(new List<ProductMappingUserAssociation>());
        _mockLinkRepo.Setup(x => x.GetAll()).ReturnsAsync(new List<PatreonAccountLink>());
        _mockPatreonApi.Setup(x => x.GetAllCampaignMembers()).ReturnsAsync(new List<PatreonMember>());

        _mockRewardService.Setup(x => x.RevokeReward("ra-1", It.IsAny<string>()))
            .ThrowsAsync(new System.InvalidOperationException("simulated"));
        _mockRewardService.Setup(x => x.RevokeReward("ra-2", It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var result = await _service.RevokeOrphans(new HashSet<string> { bubu }, actor);

        Assert.That(result.AssignmentsRevoked, Is.EqualTo(1));
        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.That(result.Errors[0], Does.Contain("ra-1"));
    }
}
