using System;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace W3ChampionsStatisticService.Services.Tracing;

/// <summary>
/// Application Insights counterpart of <see cref="TelemetryRedactionProcessor"/>: request telemetry records the
/// full request URL including its query, and HTTP dependency telemetry records the outbound path in its name and
/// the full URL in its data. Every query value of that data is redacted, keys kept, because outbound clients send
/// credentials as query parameters. Initializers run again when the telemetry is tracked, after those fields are set.
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
                dependency.Data = TelemetryRedaction.RedactUrlQueryValues(dependency.Data);
                break;
        }
    }
}
