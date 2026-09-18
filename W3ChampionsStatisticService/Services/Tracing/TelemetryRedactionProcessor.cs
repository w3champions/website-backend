using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenTelemetry;

namespace W3ChampionsStatisticService.Services.Tracing;

/// <summary>
/// Applies <see cref="TelemetryRedaction"/> to every ended span, whichever instrumentation set its attributes: the
/// URL attributes, and the header attributes an instrumentation records credential headers under. Neither the
/// ASP.NET Core nor the HttpClient instrumentation records headers or bodies, and AddW3CTracing's enrich hooks read
/// only x-faro-session-id; should a hook or an option ever record headers, the x-proof-hash value still never leaves
/// (spec §10.3). The URL redaction covers the shapes that used to carry a proofHash and the credentials still sent as
/// query values: the HttpClient instrumentation redacts query values only, and the ASP.NET Core instrumentation's
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
        RedactHeaderTags(data);
    }

    /// <summary>The keys are collected first: setting a tag while enumerating the tags is not allowed.</summary>
    private static void RedactHeaderTags(Activity activity)
    {
        List<string> secretTags = null;
        foreach (var tag in activity.TagObjects)
        {
            if (tag.Value != null && !TelemetryRedaction.Redacted.Equals(tag.Value) && TelemetryRedaction.IsSecretHeaderKey(tag.Key))
            {
                (secretTags ??= []).Add(tag.Key);
            }
        }

        if (secretTags == null)
        {
            return;
        }

        foreach (var tag in secretTags)
        {
            activity.SetTag(tag, TelemetryRedaction.Redacted);
        }
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
