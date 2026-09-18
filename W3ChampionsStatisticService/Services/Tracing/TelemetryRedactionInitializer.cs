using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace W3ChampionsStatisticService.Services.Tracing;

/// <summary>
/// Application Insights counterpart of <see cref="TelemetryRedactionProcessor"/>: request telemetry records the
/// full request URL including its query, and HTTP dependency telemetry records the outbound path in its name and
/// the full URL in its data. Every query value of an HTTP URL in that data is redacted, keys kept, because outbound
/// clients send credentials as query parameters. Other dependency data (a command or query text) keeps its query-like
/// text; only credential values and proofHash segments are redacted there. No module or initializer of this service
/// copies request headers into telemetry (the request module records the URL, not the headers); should one ever do
/// so, a property named after a credential header is redacted on every kind of telemetry. Initializers run again
/// when the telemetry is tracked, after those fields are set.
/// </summary>
public sealed class TelemetryRedactionInitializer : ITelemetryInitializer
{
    public void Initialize(ITelemetry telemetry)
    {
        switch (telemetry)
        {
            case RequestTelemetry request when request.Url != null:
                var url = request.Url.OriginalString;
                var redactedUrl = TelemetryRedaction.RedactUrl(url);
                if (!ReferenceEquals(redactedUrl, url))
                {
                    request.Url = new Uri(redactedUrl, UriKind.RelativeOrAbsolute);
                }
                break;
            case DependencyTelemetry dependency:
                dependency.Name = TelemetryRedaction.RedactUrl(dependency.Name);
                dependency.Data = IsHttpUrl(dependency.Data)
                    ? TelemetryRedaction.RedactUrlQueryValues(dependency.Data)
                    : TelemetryRedaction.RedactUrl(dependency.Data);
                break;
        }

        if (telemetry is ISupportProperties { Properties.Count: > 0 } withProperties)
        {
            RedactSecretHeaderProperties(withProperties.Properties);
        }
    }

    /// <summary>The keys are collected first: the dictionary may not be written while it is enumerated.</summary>
    private static void RedactSecretHeaderProperties(IDictionary<string, string> properties)
    {
        List<string> secretKeys = null;
        foreach (var property in properties)
        {
            if (property.Value != null && property.Value != TelemetryRedaction.Redacted && TelemetryRedaction.IsSecretHeaderKey(property.Key))
            {
                (secretKeys ??= []).Add(property.Key);
            }
        }

        if (secretKeys == null)
        {
            return;
        }

        foreach (var key in secretKeys)
        {
            properties[key] = TelemetryRedaction.Redacted;
        }
    }

    /// <summary>
    /// An HTTP dependency is recognised by the URL in its data, not by its type: Application Insights'
    /// HttpDependenciesParsingTelemetryInitializer is registered before this initializer and renames some HTTP
    /// dependencies ("WCF Service", "Azure blob", ...) while keeping the request URL. HttpClient accepts a request
    /// URL with leading whitespace (a misconfigured base URL) and the collector records it verbatim, so the scheme
    /// is looked for past leading whitespace and a byte-order mark, which char.IsWhiteSpace does not cover.
    /// </summary>
    private static bool IsHttpUrl(string data)
    {
        if (data == null)
        {
            return false;
        }

        var start = 0;
        while (start < data.Length && (char.IsWhiteSpace(data[start]) || data[start] == '\uFEFF'))
        {
            start++;
        }

        var url = data.AsSpan(start);
        return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
    }
}
