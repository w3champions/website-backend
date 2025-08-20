using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Repositories;
using W3ChampionsStatisticService.Rewards.Providers.Patreon;

namespace W3ChampionsStatisticService.Rewards.Services;

public class PatreonDriftDetectionService(
    PatreonApiClient patreonApiClient,
    IRewardAssignmentRepository assignmentRepository)
{
    private readonly PatreonApiClient _patreonApiClient = patreonApiClient;
    private readonly IRewardAssignmentRepository _assignmentRepository = assignmentRepository;
    private const string ProviderId = "patreon";

    public async Task<DriftDetectionResult> DetectDrift()
    {
        try
        {
            Log.Information("Starting Patreon drift detection");

            // Fetch current state from Patreon API
            var patreonMembers = await _patreonApiClient.GetAllCampaignMembers();

            // Fetch our internal state
            var internalAssignments = await GetActivePatreonAssignments();

            // Analyze the drift
            var result = AnalyzeDrift(patreonMembers, internalAssignments);

            // Log the results
            LogDriftResults(result);

            Log.Information("Patreon drift detection completed. Found {MissingCount} missing members, {ExtraCount} extra assignments, {MismatchedCount} mismatched tiers",
                result.MissingMembers.Count, result.ExtraAssignments.Count, result.MismatchedTiers.Count);

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during Patreon drift detection");
            throw;
        }
    }

    private async Task<List<RewardAssignment>> GetActivePatreonAssignments()
    {
        // Get all active assignments for Patreon provider
        var assignments = await _assignmentRepository.GetActiveAssignmentsByProvider(ProviderId);
        return assignments;
    }

    private DriftDetectionResult AnalyzeDrift(List<PatreonMember> patreonMembers, List<RewardAssignment> internalAssignments)
    {
        var result = new DriftDetectionResult
        {
            Timestamp = DateTime.UtcNow,
            ProviderId = ProviderId
        };

        // Create lookup dictionaries for efficient comparison
        var patreonByEmail = patreonMembers
            .Where(m => !string.IsNullOrEmpty(m.Email))
            .GroupBy(m => m.Email.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        var internalByUserId = internalAssignments
            .GroupBy(a => a.UserId.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.ToList());

        // Find missing members (in Patreon but not in our system)
        foreach (var patreonMember in patreonMembers.Where(m => m.IsActivePatron))
        {
            if (string.IsNullOrEmpty(patreonMember.Email))
            {
                Log.Warning("Patreon member {Id} has no email, skipping drift detection", patreonMember.Id);
                continue;
            }

            var userIdKey = patreonMember.Email.ToLowerInvariant();

            if (!internalByUserId.ContainsKey(userIdKey))
            {
                result.MissingMembers.Add(new MissingMember
                {
                    PatreonMemberId = patreonMember.Id,
                    Email = patreonMember.Email,
                    PatronStatus = patreonMember.PatronStatus,
                    EntitledTierIds = patreonMember.EntitledTierIds,
                    Reason = "Active patron found in Patreon but no active rewards in our system"
                });
            }
            else
            {
                // Check if tiers match
                var assignments = internalByUserId[userIdKey];
                var internalTierIds = ExtractTierIdsFromAssignments(assignments);

                if (!AreTierSetsEqual(patreonMember.EntitledTierIds, internalTierIds))
                {
                    result.MismatchedTiers.Add(new TierMismatch
                    {
                        UserId = userIdKey,
                        PatreonMemberId = patreonMember.Id,
                        PatreonTiers = patreonMember.EntitledTierIds,
                        InternalTiers = internalTierIds,
                        Reason = "Tier entitlements don't match between Patreon and internal state"
                    });
                }
            }
        }

        // Find extra assignments (in our system but not active in Patreon)
        foreach (var kvp in internalByUserId)
        {
            var userId = kvp.Key;
            var assignments = kvp.Value;

            if (!patreonByEmail.ContainsKey(userId))
            {
                // No matching Patreon member found
                result.ExtraAssignments.AddRange(assignments.Select(a => new ExtraAssignment
                {
                    AssignmentId = a.Id,
                    UserId = a.UserId,
                    RewardId = a.RewardId,
                    AssignedAt = a.AssignedAt,
                    Reason = "Active reward assignment but no corresponding active Patreon member"
                }));
            }
            else
            {
                var patreonMember = patreonByEmail[userId];
                if (!patreonMember.IsActivePatron)
                {
                    // Patreon member exists but is not active
                    result.ExtraAssignments.AddRange(assignments.Select(a => new ExtraAssignment
                    {
                        AssignmentId = a.Id,
                        UserId = a.UserId,
                        RewardId = a.RewardId,
                        AssignedAt = a.AssignedAt,
                        PatreonStatus = patreonMember.PatronStatus,
                        Reason = $"Patreon member is not active (status: {patreonMember.PatronStatus}, charge: {patreonMember.LastChargeStatus})"
                    }));
                }
            }
        }

        // Calculate summary statistics
        result.TotalPatreonMembers = patreonMembers.Count;
        result.ActivePatreonMembers = patreonMembers.Count(m => m.IsActivePatron);
        result.TotalInternalAssignments = internalAssignments.Count;
        result.UniqueInternalUsers = internalByUserId.Count;

        return result;
    }

    private List<string> ExtractTierIdsFromAssignments(List<RewardAssignment> assignments)
    {
        var tierIds = new HashSet<string>();

        foreach (var assignment in assignments)
        {
            // Extract tier IDs from metadata if available
            if (assignment.Metadata != null && assignment.Metadata.TryGetValue("tier_id", out var tierIdObj))
            {
                if (tierIdObj is string tierId)
                {
                    tierIds.Add(tierId);
                }
            }

            // Also check provider reference which might contain tier information
            if (!string.IsNullOrEmpty(assignment.ProviderReference))
            {
                // Provider reference might be in format "member_id:tier_id"
                var parts = assignment.ProviderReference.Split(':');
                if (parts.Length > 1)
                {
                    tierIds.Add(parts[1]);
                }
            }
        }

        return tierIds.ToList();
    }

    private bool AreTierSetsEqual(List<string> patreonTiers, List<string> internalTiers)
    {
        if (patreonTiers == null && internalTiers == null) return true;
        if (patreonTiers == null || internalTiers == null) return false;

        var patreonSet = new HashSet<string>(patreonTiers);
        var internalSet = new HashSet<string>(internalTiers);

        return patreonSet.SetEquals(internalSet);
    }

    private void LogDriftResults(DriftDetectionResult result)
    {
        if (result.HasDrift)
        {
            Log.Warning("Patreon drift detected! Missing: {Missing}, Extra: {Extra}, Mismatched: {Mismatched}",
                result.MissingMembers.Count, result.ExtraAssignments.Count, result.MismatchedTiers.Count);

            // Log details for missing members
            foreach (var missing in result.MissingMembers.Take(10)) // Log first 10 to avoid spam
            {
                Log.Warning("Missing member: Email={Email}, PatreonId={Id}, Status={Status}, Tiers={Tiers}",
                    missing.Email, missing.PatreonMemberId, missing.PatronStatus,
                    string.Join(",", missing.EntitledTierIds ?? new List<string>()));
            }

            // Log details for extra assignments
            foreach (var extra in result.ExtraAssignments.Take(10))
            {
                Log.Warning("Extra assignment: UserId={UserId}, AssignmentId={Id}, Reason={Reason}",
                    extra.UserId, extra.AssignmentId, extra.Reason);
            }

            // Log tier mismatches
            foreach (var mismatch in result.MismatchedTiers.Take(10))
            {
                Log.Warning("Tier mismatch for user {UserId}: Patreon={PatreonTiers}, Internal={InternalTiers}",
                    mismatch.UserId,
                    string.Join(",", mismatch.PatreonTiers ?? new List<string>()),
                    string.Join(",", mismatch.InternalTiers ?? new List<string>()));
            }

            if (result.MissingMembers.Count > 10 || result.ExtraAssignments.Count > 10 || result.MismatchedTiers.Count > 10)
            {
                Log.Warning("More drift entries exist but were not logged to avoid spam. Total counts: Missing={Missing}, Extra={Extra}, Mismatched={Mismatched}",
                    result.MissingMembers.Count, result.ExtraAssignments.Count, result.MismatchedTiers.Count);
            }
        }
        else
        {
            Log.Information("No Patreon drift detected. System is in sync.");
        }

        // Always log summary statistics
        Log.Information("Patreon drift detection summary: PatreonTotal={PatreonTotal}, PatreonActive={PatreonActive}, InternalAssignments={InternalTotal}, InternalUsers={InternalUsers}",
            result.TotalPatreonMembers, result.ActivePatreonMembers, result.TotalInternalAssignments, result.UniqueInternalUsers);
    }
}

public class DriftDetectionResult
{
    public DateTime Timestamp { get; set; }
    public string ProviderId { get; set; }
    public List<MissingMember> MissingMembers { get; set; } = new();
    public List<ExtraAssignment> ExtraAssignments { get; set; } = new();
    public List<TierMismatch> MismatchedTiers { get; set; } = new();

    // Summary statistics
    public int TotalPatreonMembers { get; set; }
    public int ActivePatreonMembers { get; set; }
    public int TotalInternalAssignments { get; set; }
    public int UniqueInternalUsers { get; set; }

    public bool HasDrift => MissingMembers.Any() || ExtraAssignments.Any() || MismatchedTiers.Any();
}

public class MissingMember
{
    public string PatreonMemberId { get; set; }
    public string Email { get; set; }
    public string PatronStatus { get; set; }
    public List<string> EntitledTierIds { get; set; }
    public string Reason { get; set; }
}

public class ExtraAssignment
{
    public string AssignmentId { get; set; }
    public string UserId { get; set; }
    public string RewardId { get; set; }
    public DateTime AssignedAt { get; set; }
    public string PatreonStatus { get; set; }
    public string Reason { get; set; }
}

public class TierMismatch
{
    public string UserId { get; set; }
    public string PatreonMemberId { get; set; }
    public List<string> PatreonTiers { get; set; }
    public List<string> InternalTiers { get; set; }
    public string Reason { get; set; }
}