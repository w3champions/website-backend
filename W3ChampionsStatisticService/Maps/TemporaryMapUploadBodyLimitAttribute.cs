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
/// Two actions stream a map file of that size: the temporary upload and the admin map-file passthrough
/// (<see cref="MapsController.CreateMapFile"/>). The global Kestrel limit (<c>MaxRequestBodySize</c> in Program.cs,
/// 128 MiB) and Kestrel's default data-rate floor stay in force everywhere else. Resource filters run after
/// authorization filters and before model binding, so both are in place before the first byte is read and a 401 from
/// an authorization filter still wins first. The passthrough's permission check is an action filter, so there both are
/// set before its 401; see <see cref="MapsController.CreateMapFile"/> for why that is bounded and which test pins it.
/// <para>
/// The data-rate floor bounds how long one request can keep streaming: below the floor Kestrel flags the request and
/// cancels the pending read, and the read surfaces as its 408 <c>BadHttpRequestException</c> (RequestBodyTimeout) with
/// <c>RequestAborted</c> not cancelled. On the temporary upload that is what keeps an in-flight slot from being held for
/// days by a client that trickles its body: the action aborts the connection and answers nothing, and the slot and the
/// partial spool are released. On the passthrough the failed read fails the forward. A server without the feature
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
                "Map upload body limit not raised: the request body was already being read (limit stays {MaxRequestBodySize})",
                feature.MaxRequestBodySize);
            return;
        }

        feature.MaxRequestBodySize = TemporaryMapLimits.TransportBodyBytes;
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
