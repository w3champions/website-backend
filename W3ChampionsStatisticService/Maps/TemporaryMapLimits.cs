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

    /// <summary>
    /// Upload attempts per battleTag per hour, spent once an upload holds an in-flight slot and before its body is read,
    /// whatever the attempt then turns into (a new record, a dedupe hit, a rejection, an abandoned body). It bounds how
    /// often one account can take a slot at all; <see cref="UploadsPerHourPerBattleTag"/> separately bounds the new
    /// records it can create. The same 429 QUOTA_EXCEEDED body answers both.
    /// </summary>
    public const int UploadAttemptsPerHourPerBattleTag = 20;

    /// <summary>
    /// The least rate a client must sustain while sending an upload body, after <see cref="MinUploadGracePeriod"/>: below
    /// it Kestrel aborts the request, which frees the in-flight slot. Kestrel's own floor (240 B/s after 5 s) would let a
    /// 256 MiB body take about thirteen days, so eight trickling connections could hold every slot for as long as they
    /// liked. At this floor a full-size upload must finish in roughly 2.3 hours.
    /// </summary>
    public const int MinUploadBytesPerSecond = 32 * 1024;

    /// <summary>How long a client may fall below <see cref="MinUploadBytesPerSecond"/> before the request is aborted.</summary>
    public static readonly TimeSpan MinUploadGracePeriod = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Uploads in flight per process, across all accounts (Task 2 security H1): bounds the spooled temp disk to
    /// 8 × <see cref="MaxFileBytes"/>. One upload per battleTag is enforced at the same point (D7); both refusals answer
    /// 429 QUOTA_EXCEEDED with <see cref="ConcurrentUploadRetryAfterSeconds"/>.
    /// </summary>
    public const int MaxConcurrentUploads = 8;

    /// <summary>retryAfterSeconds of the 429 for an upload refused by <see cref="TemporaryMapUploadGate"/>.</summary>
    public const int ConcurrentUploadRetryAfterSeconds = 30;

    /// <summary>Days since the last game START after which a temporary map's file is deleted.</summary>
    public const int TtlDays = 30;

    public const int SweepIntervalHours = 24;
    public const int SweepBatchSize = 200;

    /// <summary>
    /// A CustomGames/ file younger than this is never treated as an orphan: an upload may be in flight. The reconciliation
    /// pass asks update-service for files at least this old (its <c>olderThanHours</c>) and nothing younger is ever deleted.
    /// </summary>
    public const int OrphanMinAgeHours = 24;

    /// <summary>
    /// Stored files the reconciliation pass reclaims (deletes) in one run, at most. Reclaiming rests on one
    /// matchmaking answer per file — "no record claims this path" — so a regression in that lookup would otherwise
    /// call every stored file unclaimed and delete all of them in one run. Past the cap, candidates are counted and
    /// left for the next run (a rate bound, never a skip) and the run ends with one Error line.
    /// </summary>
    public const int MaxReclaimsPerRun = 25;

    /// <summary>
    /// A spool file in <see cref="TempUploadDir"/> not written for this long was left behind by a crash or restart (a live
    /// upload writes continuously and finishes within minutes) and is purged by the expiry sweep (Task 2 L2).
    /// </summary>
    public const int StaleSpoolFileAgeHours = 24;

    public const int MaxSanitisedNameLength = 100;

    /// <summary>
    /// Longest `originalFileName` accepted, in UTF-16 code units: no file system hands a launcher a longer name, and
    /// matchmaking stores and re-serves the value as sent (S-L5).
    /// </summary>
    public const int MaxOriginalFileNameLength = 255;

    /// <summary>
    /// The single path prefix that makes a stored map "temporary". Forward slashes, case preserved. Defined once in
    /// <see cref="TemporaryMapKeys.PathPrefix"/>, which the update-service client's delete guard also uses.
    /// </summary>
    public const string TempMapPathPrefix = TemporaryMapKeys.PathPrefix;

    public static readonly TimeSpan UploadQuotaWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan UploadAttemptWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan PrecheckQuotaWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Spool directory for in-flight uploads. Files here are deleted in a finally block; what a crash leaves behind is
    /// purged by <see cref="TemporaryMapExpirySweep"/> once it is <see cref="StaleSpoolFileAgeHours"/> old.
    /// </summary>
    public static string TempUploadDir => Path.Combine(Path.GetTempPath(), "w3c-map-uploads");
}
