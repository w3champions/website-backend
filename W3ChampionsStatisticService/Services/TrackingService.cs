using Microsoft.ApplicationInsights;
using System.Collections.Generic;
using System;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Services;

public interface ITrackingService
{
    void TrackException(Exception ex, string message);
}

[Trace]
public class TrackingService(TelemetryClient telemetry) : ITrackingService
{
    private readonly TelemetryClient _telemetry = telemetry;

    public void TrackException(Exception ex, string message)
    {
        var additionalFields = new Dictionary<string, string>()
        {
            { "W3C_Message", message },
        };
        _telemetry.TrackException(ex, additionalFields);
    }
}
