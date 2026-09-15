using Prometheus;

namespace W3ChampionsStatisticService.Maps;

/// <summary>Design spec §13. Labels are outcome classes, never identifiers and never secrets.</summary>
public static class TemporaryMapMetrics
{
    /// <summary>
    /// One increment per upload that reaches a terminal outcome; a client abort is not counted. Upstream and
    /// orchestration faults (5xx answers, TEMP_MAP_KEY_MISMATCH included) count as <see cref="Results.UpstreamError"/>,
    /// a local spool or disk fault as <see cref="Results.ServerError"/>, and client-caused 4xx answers (including a
    /// malformed or oversized body) as <see cref="Results.Rejected"/>.
    /// </summary>
    public static readonly Counter Uploads = Metrics.CreateCounter(
        "website_temporary_map_uploads_total",
        "Temporary (self-provided) custom map upload attempts by outcome.",
        new CounterConfiguration { LabelNames = ["result"] });

    public static class Results
    {
        public const string Created = "created";
        public const string Deduped = "deduped";
        public const string Restored = "restored";
        public const string Rejected = "rejected";
        public const string UpstreamError = "upstream_error";
        public const string ServerError = "server_error";
    }
}
