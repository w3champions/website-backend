using Microsoft.ApplicationInsights;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using System;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Services;

public interface ITrackingService
{
    void TrackUnauthorizedRequest(string authorization, ControllerBase controller);
    void TrackException(Exception ex, string message);
}

[Trace]
public class TrackingService(TelemetryClient telemetry) : ITrackingService
{
    private readonly TelemetryClient _telemetry = telemetry;

    public void TrackUnauthorizedRequest(string authorization, ControllerBase controller)
    {
        try
        {
            var properties = new Dictionary<string, string>
            {
                { "RemoteIp", controller.ControllerContext.HttpContext.Connection.RemoteIpAddress.ToString() },
                { "UsedAuth", authorization },
                { "RequestPath", controller.Request.Path },
                { "RequestMethod", controller.Request.Method },
                { "RequestProtocol", controller.Request.Protocol },
                { "RequestQueryString", controller.Request.QueryString.ToString() }
            };

            _telemetry.TrackEvent("UnauthorizedRequest", properties);
        }
        catch (Exception ex)
        {
            _telemetry.TrackException(ex);
        }
    }

    public void TrackException(Exception ex, string message)
    {
        var additionalFields = new Dictionary<string, string>()
        {
            { "W3C_Message", message },
        };
        _telemetry.TrackException(ex, additionalFields);
    }
}
