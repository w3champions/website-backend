using System;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace W3ChampionsStatisticService.Services.Tracing;

/// <summary>Redacts machine IDs in the Application Insights request and dependency telemetry (when APP_INSIGHTS is set).</summary>
public sealed class MachineIdTelemetryInitializer : ITelemetryInitializer
{
    public void Initialize(ITelemetry telemetry)
    {
        switch (telemetry)
        {
            case RequestTelemetry request:
                request.Name = MachineIdRedaction.Redact(request.Name);
                if (request.Url != null)
                {
                    var url = request.Url.OriginalString;
                    var redacted = MachineIdRedaction.Redact(url);
                    if (!ReferenceEquals(url, redacted)) request.Url = new Uri(redacted, UriKind.RelativeOrAbsolute);
                }
                break;
            case DependencyTelemetry dependency:
                dependency.Name = MachineIdRedaction.Redact(dependency.Name);
                dependency.Data = MachineIdRedaction.Redact(dependency.Data);
                break;
        }
    }
}
