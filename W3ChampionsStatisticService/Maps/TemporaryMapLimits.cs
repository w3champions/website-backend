using System;
using System.IO;
using W3C.Domain.Maps;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// Every limit for the self-provided (temporary) custom-map feature. Hard-coded by owner decision:
/// design spec §A.9 states this feature introduces NO new environment variables in any service.
/// </summary>
public static class TemporaryMapLimits
{
    /// <summary>Hard cap on the spooled map file. Exceeding it is 413 { code: "FILE_TOO_LARGE" }.</summary>
    public const long MaxFileBytes = 268_435_456; // 256 MiB

    /// <summary>
    /// Per-action transport ceiling = MaxFileBytes + 1 MiB of multipart/header slack. The same value
    /// is configured at every hop (nginx-proxy client_max_body_size 257M, update-service
    /// [RequestSizeLimit]); they must not drift.
    /// </summary>
    public const long TransportBodyBytes = 269_484_032;

    /// <summary>Cap on the JSON `metadata` multipart part (Appendix A.3).</summary>
    public const int MaxMetadataBytes = 65_536; // 64 KiB

    public const int UploadsPerHourPerBattleTag = 10;
    public const int PrecheckPerBattleTagPerMinute = 60;

    /// <summary>Days since the last game START after which a temporary map's file is deleted.</summary>
    public const int TtlDays = 30;

    public const int SweepIntervalHours = 24;
    public const int SweepBatchSize = 200;

    /// <summary>A CustomGames/ file younger than this is never treated as an orphan: an upload may be in flight.</summary>
    public const int OrphanMinAgeHours = 24;

    public const int MaxSanitisedNameLength = 100;

    /// <summary>
    /// The single path prefix that makes a stored map "temporary". Forward slashes, case preserved. Defined once in
    /// <see cref="TemporaryMapKeys.PathPrefix"/>, which the update-service client's delete guard also uses.
    /// </summary>
    public const string TempMapPathPrefix = TemporaryMapKeys.PathPrefix;

    public static readonly TimeSpan UploadQuotaWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan PrecheckQuotaWindow = TimeSpan.FromMinutes(1);

    /// <summary>Spool directory for in-flight uploads. Files here are deleted in a finally block.</summary>
    public static string TempUploadDir => Path.Combine(Path.GetTempPath(), "w3c-map-uploads");
}
