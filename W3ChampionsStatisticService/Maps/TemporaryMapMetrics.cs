using System;
using System.IO;
using Prometheus;

namespace W3ChampionsStatisticService.Maps;

/// <summary>Design spec §13. Labels are outcome classes, never identifiers and never secrets.</summary>
public static class TemporaryMapMetrics
{
    /// <summary>
    /// One increment per upload that reaches a terminal outcome; a client abort is not counted. The label follows the
    /// status the controller answers: a client-caused 4xx (an A.3 rejection, a body that could not be read, Kestrel's own
    /// request errors, a refused in-flight slot) is <see cref="Results.Rejected"/>; an upstream or orchestration fault
    /// answered with a 5xx A.3 body (UPSTREAM, PARSER_MISMATCH, TEMP_MAP_KEY_MISMATCH) is <see cref="Results.UpstreamError"/>;
    /// a fault of this service answered as a bare 500 (a spool or disk fault, a body-stream fault, a cancellation the
    /// request did not cause) is <see cref="Results.ServerError"/>. See <see cref="ResultOf"/>.
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

    /// <summary>
    /// The result label of an upload that escaped the service with <paramref name="exception"/>, classified by the type
    /// alone so that it matches the status <see cref="TemporaryMapsController"/> answers it with (see the catch ladder
    /// there): the one place this classification is spelled.
    /// </summary>
    public static string ResultOf(Exception exception) => exception switch
    {
        // Its status is relayed: below 500 the client did something wrong; PARSER_MISMATCH, UPSTREAM and
        // TEMP_MAP_KEY_MISMATCH are the upstream's or the orchestration's fault.
        TemporaryMapUploadException { StatusCode: < 500 } => Results.Rejected,
        TemporaryMapUploadException => Results.UpstreamError,
        // The request body could not be read (Kestrel's BadHttpRequestException included): 413 or 400, or Kestrel's
        // 408 for a body below the data-rate floor, on which the controller aborts the connection and answers nothing.
        // Still the client's doing, so still rejected — unlike a client that went away, which is counted nowhere.
        IOException => Results.Rejected,
        // A spool fault, a body-stream fault, a cancellation the request did not cause: a bare 500 of this service.
        _ => Results.ServerError,
    };
}
