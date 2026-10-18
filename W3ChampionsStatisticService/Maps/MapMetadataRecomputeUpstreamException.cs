using System;

namespace W3ChampionsStatisticService.Maps;

/// <summary>A sibling service failed before anything was patched in matchmaking.</summary>
public class MapMetadataRecomputeUpstreamException(string service, int? upstreamStatus, string message, Exception inner)
    : Exception(message, inner)
{
    public const string Matchmaking = "matchmaking";
    public const string UpdateService = "update-service";

    public string Service { get; } = service;
    /// <summary>Null when the service was unreachable or timed out.</summary>
    public int? UpstreamStatus { get; } = upstreamStatus;
    /// <summary>Update-service paths whose chunk completed before the failure (in a non-dry run those rows are already written).</summary>
    public int PathsCompleted { get; init; }
    public int PathsTotal { get; init; }
}
