using System.Collections.Generic;
using W3C.Domain.UpdateService.Contracts;

namespace W3ChampionsStatisticService.Maps;

public class MapMetadataRecomputeBody
{
    /// <summary>Defaults to true: only an explicit <c>false</c> writes anything.</summary>
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// Optional: only these matchmaking map ids are processed (ids matchmaking does not know are reported).
    /// Omitted means every map; an empty list is rejected.
    /// </summary>
    public int[] MapIds { get; set; }
}

public class MapMetadataRecomputeReport
{
    public bool DryRun { get; set; }
    public MapMetadataRecomputeSummary Summary { get; set; }
    /// <summary>One row per matchmaking map, including maps that could not be recomputed.</summary>
    public List<MapMetadataRecomputeMapReport> Maps { get; set; } = [];
    /// <summary>Update-service results that no matchmaking map could be joined to.</summary>
    public List<MapMetadataRecomputeResult> UnmatchedResults { get; set; } = [];
    /// <summary>Requested map ids matchmaking does not know.</summary>
    public List<int> UnknownMapIds { get; set; } = [];
    /// <summary>Set when a matchmaking patch call failed; update-service has already written its rows.</summary>
    public string MmPatchError { get; set; }
}

public class MapMetadataRecomputeSummary
{
    public int MmMaps { get; set; }
    public int PathsSent { get; set; }
    public int Unchanged { get; set; }
    public int Changed { get; set; }
    /// <summary>Maps whose row has a <see cref="MapMetadataRecomputeMapReport.Problem"/>.</summary>
    public int Problems { get; set; }
    public int UnmatchedResults { get; set; }
    public int UnknownMapIds { get; set; }
    /// <summary>Distinct sha1s matchmaking needs (or, in a dry run, would need) patched.</summary>
    public int MmPatchNeeded { get; set; }
    /// <summary>Distinct sha1s sent to matchmaking, including those of a chunk whose call failed.</summary>
    public int MmPatchSent { get; set; }
    public int MmMatched { get; set; }
    public int MmModified { get; set; }
}

public class MapMetadataRecomputeMapReport
{
    public int MmMapId { get; set; }
    public string Name { get; set; }
    /// <summary>Matchmaking gameMap.path.</summary>
    public string Path { get; set; }
    public string UpdateServicePath { get; set; }
    /// <summary>Matchmaking gameMap.sha1.</summary>
    public string Sha1 { get; set; }
    public string UpdateServiceSha1 { get; set; }
    /// <summary>What matchmaking holds right now.</summary>
    public long? MmChecksum { get; set; }
    public long? MmCrc32 { get; set; }
    /// <summary>What update-service held before the recompute.</summary>
    public long? OldChecksum { get; set; }
    public long? NewChecksum { get; set; }
    public long? OldCrc32 { get; set; }
    public long? NewCrc32 { get; set; }
    /// <summary>The update-service status, or a <see cref="MapMetadataRecomputeProblems"/> code when there is no result.</summary>
    public string Status { get; set; }
    public string Error { get; set; }
    public bool MmPatchNeeded { get; set; }
    /// <summary>True only when matchmaking reported a match for this map's sha1.</summary>
    public bool MmPatched { get; set; }
    public int? MmMatched { get; set; }
    public int? MmModified { get; set; }
    /// <summary>Null when the map is fine; otherwise why it was not (or could not be) fixed.</summary>
    public string Problem { get; set; }
}

/// <summary>Problem codes besides the failing update-service statuses, which are reported as they are.</summary>
public static class MapMetadataRecomputeProblems
{
    public const string NoPath = "no_path";
    public const string NoResult = "no_result";
    /// <summary>Update-service holds a different file (sha1) under this map's path than matchmaking references.</summary>
    public const string Sha1Differs = "sha1_differs";
    /// <summary>Defensive: a changed/unchanged result that still carries an error is treated as not stored.</summary>
    public const string UpdateServiceWriteFailed = "update_service_write_failed";
    /// <summary>A recomputed checksum/crc32 is missing or not an unsigned 32-bit integer, which matchmaking rejects.</summary>
    public const string InvalidNewValues = "invalid_new_values";
    public const string ConflictingValues = "conflicting_values";
    /// <summary>Matchmaking's gameMap.sha1 is not 40 lowercase hex characters, so it cannot be patched by sha1.</summary>
    /// <summary>
    /// Another path with the same sha1 failed on update-service. The matchmaking patch is keyed by sha1 and would
    /// also hit that map, so nothing with this sha1 is patched.
    /// </summary>
    public const string Sha1HasFailedResult = "sha1_has_failed_result";
    public const string MmSha1NotLowercaseHex = "mm_sha1_not_lowercase_hex";
    public const string MmPatchFailed = "mm_patch_failed";
    public const string MmNoResult = "mm_no_result";
    public const string MmNotMatched = "mm_not_matched";
}
