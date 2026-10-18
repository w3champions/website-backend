using System.Collections.Generic;
using Newtonsoft.Json;

namespace W3C.Domain.UpdateService.Contracts;

/// <summary>Body of update-service <c>POST /api/content/maps/metadata/recompute</c>.</summary>
public class MapMetadataRecomputeRequest
{
    [JsonProperty("dryRun")]
    public bool DryRun { get; set; }

    /// <summary>Update-service file paths ("W3Champions/x.w3x"). Never send null: update-service reads that as "every file".</summary>
    [JsonProperty("paths")]
    public List<string> Paths { get; set; } = [];
}

public class MapMetadataRecomputeResponse
{
    /// <summary>Nullable so a missing value is told apart from <c>false</c> and rejected by the caller.</summary>
    public bool? DryRun { get; set; }
    public List<MapMetadataRecomputeResult> Results { get; set; }
}

public class MapMetadataRecomputeResult
{
    public string Path { get; set; }
    public string MapDataId { get; set; }
    public string Sha1 { get; set; }
    public long? OldChecksum { get; set; }
    public long? NewChecksum { get; set; }
    public long? OldCrc32 { get; set; }
    public long? NewCrc32 { get; set; }
    public bool Changed { get; set; }
    /// <summary>One of <see cref="MapMetadataRecomputeStatus"/>.</summary>
    public string Status { get; set; }
    public string Error { get; set; }
}

public static class MapMetadataRecomputeStatus
{
    public const string Unchanged = "unchanged";
    public const string Changed = "changed";
    public const string MissingFile = "missing_file";
    public const string ParseError = "parse_error";
    public const string Sha1Mismatch = "sha1_mismatch";
    /// <summary>A non-dry run could not store the recomputed values; <c>error</c> says why.</summary>
    public const string WriteFailed = "write_failed";
}
