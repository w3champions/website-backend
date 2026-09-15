using System;
using System.Diagnostics;
using OpenTelemetry;

namespace W3ChampionsStatisticService.Services.Tracing;

/// <summary>
/// Applies <see cref="TelemetryRedaction"/> to the URL attributes of every ended span, whichever instrumentation
/// set them. The HttpClient instrumentation redacts query values only, so the proofHash path segment of
/// matchmaking's by-proof-hash route otherwise reaches <c>url.full</c> verbatim; the ASP.NET Core instrumentation's
/// own <c>url.query</c> redaction can be switched off by configuration. Register it before the exporter.
/// </summary>
public sealed class TelemetryRedactionProcessor : BaseProcessor<Activity>
{
    private const string UrlFull = "url.full";
    private const string UrlPath = "url.path";
    private const string UrlQuery = "url.query";

    public override void OnEnd(Activity data)
    {
        Redact(data, UrlFull, TelemetryRedaction.RedactUrl);
        Redact(data, UrlPath, TelemetryRedaction.RedactUrl);
        Redact(data, UrlQuery, TelemetryRedaction.RedactQuery);
    }

    private static void Redact(Activity activity, string tag, Func<string, string> redact)
    {
        if (activity.GetTagItem(tag) is not string value)
        {
            return;
        }

        var redacted = redact(value);
        if (!ReferenceEquals(redacted, value))
        {
            activity.SetTag(tag, redacted);
        }
    }
}
