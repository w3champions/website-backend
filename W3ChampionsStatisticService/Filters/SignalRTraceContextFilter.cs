using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.Filters;

public class SignalRTraceContextFilter(TracingService tracingService) : IHubFilter
{
    private readonly TracingService _tracingService = tracingService;

    private class TracingContextPayload
    {
        public string TraceParent { get; set; }
        public string TraceState { get; set; }
        public string FaroSessionId { get; set; }
    }

    public async ValueTask<object> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object>> next)
    {
        string traceParent = null;
        string traceState = null;
        Dictionary<string, object> clientTags = null;

        // Extract BattleTag if user is authenticated
        string battleTag = null;
        if (invocationContext.Context.User?.Identity?.IsAuthenticated == true &&
            !string.IsNullOrEmpty(invocationContext.Context.User.Identity.Name))
        {
            battleTag = invocationContext.Context.User.Identity.Name;
            clientTags ??= [];
            clientTags[BaggageToTagProcessor.BattleTagKey] = battleTag;
        }

        if (invocationContext.HubMethodArguments.Count > 0)
        {
            var arg = invocationContext.HubMethodArguments[^1]; // We always pass the tracing context as the last argument
            if (arg is JsonElement jsonElement)
            {
                try
                {
                    // Check this for backwards compatibility
                    if (jsonElement.TryGetProperty("tracingContext", out var tracingContextElement))
                    {
                        var tracingContextPayload = tracingContextElement.Deserialize<TracingContextPayload>();
                        if (tracingContextPayload != null)
                        {
                            traceParent = tracingContextPayload.TraceParent;
                            traceState = tracingContextPayload.TraceState;
                            if (!string.IsNullOrEmpty(tracingContextPayload.FaroSessionId))
                            {
                                clientTags ??= [];
                                clientTags[BaggageToTagProcessor.SessionIdKey] = tracingContextPayload.FaroSessionId;
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Argument was not the expected JSON structure, ignore and continue.
                }
            }
        }

        var operationName = $"{invocationContext.HubMethod.DeclaringType.Name}/{invocationContext.HubMethodName}";

        if (!string.IsNullOrEmpty(traceParent))
        {
            return await _tracingService.ExecuteWithExternalParentTraceAsync(
                operationName,
                traceParent,
                traceState,
                async () => await next(invocationContext),
                clientTags);
        }
        else
        {
            // No external trace context found from client message arguments.
            // Create a new server span. It will be a root span if Activity.Current is null,
            // or a child of Activity.Current if it exists (e.g., from OnConnectedAsync).
            // clientTags will be null here if no tracingContext was found in args.
            return await _tracingService.ExecuteAsNewServerSpanAsync(operationName, async () => await next(invocationContext), clientTags);
        }
    }
}
