using System;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Serilog;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// Raises the request-body ceiling to <see cref="TemporaryMapLimits.TransportBodyBytes"/> for the
/// decorated action only. The global Kestrel limit (<c>MaxRequestBodySize</c> in Program.cs, 128 MiB)
/// stays in force everywhere else. Resource filters run after authorization filters and before model
/// binding, so the limit is in place before the first byte is read and the 401 still wins first.
/// <para>
/// A server without the feature has no per-request limit to raise. A read-only feature means the body
/// was already being read, so the server's own limit stays; that is logged, because uploads above
/// 128 MiB would then fail as FILE_TOO_LARGE for a reason the response cannot show.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class TemporaryMapUploadBodyLimitAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
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
