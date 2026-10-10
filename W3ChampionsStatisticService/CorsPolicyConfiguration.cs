using System;
using Microsoft.AspNetCore.Cors.Infrastructure;

namespace W3ChampionsStatisticService;

public static class CorsPolicyConfiguration
{
    /// <summary>
    /// Lets browsers cache preflight responses. Without Access-Control-Max-Age they cache them for ~5 s,
    /// so non-simple requests (JSON bodies, Authorization, trace headers) cost two round trips each and a
    /// reconnect burst doubles in size. Browsers clamp the value (Chromium 2 h, WebKit 10 min).
    /// </summary>
    public static readonly TimeSpan PreflightMaxAge = TimeSpan.FromHours(2);

    public static void Configure(CorsPolicyBuilder builder) =>
        builder
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(_ => true)
            .AllowCredentials()
            .SetPreflightMaxAge(PreflightMaxAge);
}
