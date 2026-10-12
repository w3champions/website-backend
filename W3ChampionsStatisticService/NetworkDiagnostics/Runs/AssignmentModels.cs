using System;
using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Runs;

public static class AssignmentStatus
{
    public const string Pending = "Pending";
    public const string Submitted = "Submitted";
    public const string Expired = "Expired";
}

/// <summary>One per run × BattleTag. _id is the measurementId. Token is stored in plaintext (contract A10).</summary>
[BsonIgnoreExtraElements]
public record NetworkDiagnosticAssignment
{
    [BsonId] public string Id { get; init; }
    public string RunId { get; init; }
    public string BattleTag { get; init; }
    /// <summary>Secret (A10). Sent only in the hub push to its owner; never logged, never in an admin DTO.</summary>
    public string Token { get; init; }
    public string Status { get; init; }
    public DateTime DeliverAfter { get; init; }
    public DateTime DeliverBy { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public DateTime ExpiresAt { get; init; }

    // Replaces the generated record ToString, which would print Token.
    public override string ToString() =>
        $"NetworkDiagnosticAssignment {{ Id = {Id}, RunId = {RunId}, BattleTag = {BattleTag}, Status = {Status}, DeliverBy = {DeliverBy:o} }}";
}

public static class AssignmentFactory
{
    public static NetworkDiagnosticAssignment Create(string runId, string battleTag, DateTime deliverAfter, DateTime deliverBy) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            RunId = runId,
            BattleTag = battleTag,
            Token = MeasurementTokens.NewToken(),
            Status = AssignmentStatus.Pending,
            DeliverAfter = deliverAfter,
            DeliverBy = deliverBy,
            ExpiresAt = deliverBy + NetDiagConstants.Retention,
        };
}
