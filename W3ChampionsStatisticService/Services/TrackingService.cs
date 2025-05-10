using Microsoft.ApplicationInsights;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using System;
using Microsoft.Extensions.Logging;

namespace W3ChampionsStatisticService.Services;

public interface ITrackingService
{
    void TrackUnauthorizedRequest(string authorization, ControllerBase controller);
    void TrackException(Exception ex, string message);
}

public class TrackingService : ITrackingService
{
    private readonly TelemetryClient _telemetry;
    private readonly ILogger<TrackingService> _logger;

    public TrackingService(
        TelemetryClient telemetry,
        ILogger<TrackingService> logger)
    {
        _telemetry = telemetry;
        _logger = logger;
    }
    public void TrackUnauthorizedRequest(string authorization, ControllerBase controller)
    {
        try
        {
            var properties = new Dictionary<string, string>();

            properties.Add("RemoteIp", controller.ControllerContext.HttpContext.Connection.RemoteIpAddress.ToString());
            properties.Add("UsedAuth", authorization);

            properties.Add("RequestPath", controller.Request.Path);
            properties.Add("RequestMethod", controller.Request.Method);
            properties.Add("RequestProtocol", controller.Request.Protocol);
            properties.Add("RequestQueryString", controller.Request.QueryString.ToString());

            _telemetry.TrackEvent("UnauthorizedRequest", properties);
        }
        catch (Exception ex)
        {
            _telemetry.TrackException(ex);
        }
    }

    public void TrackException(Exception ex, string message)
    {
        _logger.LogError(ex, message);
        _telemetry.TrackException(ex);
    }
}
