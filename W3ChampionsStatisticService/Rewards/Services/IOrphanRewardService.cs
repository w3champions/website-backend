using System.Collections.Generic;
using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Rewards.Services;

public interface IOrphanRewardService
{
    /// <summary>
    /// Detects active patreon RewardAssignments with no backing active PMUA and whose user is
    /// not an active Patreon member.
    /// </summary>
    Task<OrphanRewardReport> DetectOrphans();

    /// <summary>
    /// Revokes the orphan RewardAssignments. Always runs DetectOrphans internally to ensure
    /// the data is fresh — the admin endpoint passes the previously-shown report's user list
    /// (or a subset) for safety, but the service re-validates before any revocation.
    /// </summary>
    Task<OrphanRewardRevocationResult> RevokeOrphans(IReadOnlySet<string> approvedUserIds, string actorBattleTag);
}

/// <summary>
/// Detection result. Each entry represents one user with at least one orphan RA.
/// </summary>
public class OrphanRewardReport
{
    public System.DateTime DetectedAtUtc { get; set; }
    public List<OrphanRewardEntry> Entries { get; set; } = new();
}

public class OrphanRewardEntry
{
    public string UserId { get; set; }
    public List<OrphanAssignmentDetail> Assignments { get; set; } = new();
    public string Reason { get; set; }
}

public class OrphanAssignmentDetail
{
    public string AssignmentId { get; set; }
    public string RewardId { get; set; }
    public string ProviderReference { get; set; }
    public System.DateTime AssignedAt { get; set; }
}

public class OrphanRewardRevocationResult
{
    public System.DateTime ExecutedAtUtc { get; set; }
    public int UsersTouched { get; set; }
    public int AssignmentsRevoked { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> SkippedUserIdsNotInLatestDetection { get; set; } = new();
}
