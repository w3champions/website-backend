using System;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Serilog;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// Raises the request-body ceiling to <see cref="TemporaryMapLimits.TransportBodyBytes"/> for the
/// decorated action only, and sets the least rate the body must arrive at
/// (<see cref="TemporaryMapLimits.MinUploadBytesPerSecond"/> after <see cref="TemporaryMapLimits.MinUploadGracePeriod"/>).
/// The global Kestrel limit (<c>MaxRequestBodySize</c> in Program.cs, 128 MiB) and Kestrel's default data-rate floor
/// stay in force everywhere else. Resource filters run after authorization filters and before model
/// binding, so both are in place before the first byte is read and the 401 still wins first.
/// <para>
/// The data-rate floor is what keeps an in-flight slot from being held for days by a client that trickles its body:
/// below the floor Kestrel flags the request and cancels the pending read, the read surfaces as its 408
/// <c>BadHttpRequestException</c> (RequestBodyTimeout) with <c>RequestAborted</c> not cancelled, the action aborts the
/// connection and answers nothing, and the slot and the partial spool are released. A server without the feature
/// (a test host) has no floor to set.
/// </para>
/// <para>
/// A server without the size feature has no per-request limit to raise. A read-only feature means the body
/// was already being read, so the server's own limit stays; that is logged, because uploads above
/// 128 MiB would then fail as FILE_TOO_LARGE for a reason the response cannot show.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class TemporaryMapUploadBodyLimitAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var dataRate = context.HttpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>();
        if (dataRate != null)
        {
            dataRate.MinDataRate = new MinDataRate(TemporaryMapLimits.MinUploadBytesPerSecond, TemporaryMapLimits.MinUploadGracePeriod);
        }

        var feature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature == null)
        {
            return;
        }

        if (feature.IsReadOnly)
        {
            Log.Warning(
                "Temporary map upload body limit not raised: the request body was already being read (limit stays {MaxRequestBodySize})",
                feature.MaxRequestBodySize);
            return;
        }

        feature.MaxRequestBodySize = TemporaryMapLimits.TransportBodyBytes;
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
