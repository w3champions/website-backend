using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.Services.Tracing;
using Serilog;

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

    /// <summary>
    /// This class merely serves as placeholder. Due to the way we are tracing function calls, every SignalR invocations needs to have at least one argument.
    /// Hence, this placeholder here is used to ensure that every function has at least one argument.
    /// Please make sure that all websocket handlers have at least one argument or this placeholder here added.
    /// </summary>
    public class PreventZeroArgumentHandler
    {
        public PreventZeroArgumentHandler()
        {
        }
    }

    public async ValueTask<object> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object>> next)
    {
        string traceParent = null;
        string traceState = null;
        Dictionary<string, object> clientTags = null;
        HubInvocationContext contextToPass = invocationContext; // By default, pass the original context

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
            var lastArg = invocationContext.HubMethodArguments[^1];  // We always pass the tracing context as the last argument
            TracingContextPayload parsedPayload = null;

            if (lastArg is JsonElement jsonElement)
            {
                try
                {
                    // Attempt to deserialize the last argument directly as TracingContextPayload
                    parsedPayload = jsonElement.Deserialize<TracingContextPayload>();
                }
                catch (JsonException ex)
                {
                    // Log if parsing fails, but don't break the call if it wasn't a tracing context.
                    // This could happen if the last argument is genuinely a JsonElement for the method itself.
                    Log.Debug(ex, "[SignalRTraceContextFilter] JSON error deserializing last argument as TracingContextPayload. It might not be a tracing context.");
                }
            }

            // Check if we successfully parsed a payload AND it looks like a valid trace context (TraceParent is key)
            if (parsedPayload != null && !string.IsNullOrEmpty(parsedPayload.TraceParent))
            {
                traceParent = parsedPayload.TraceParent;
                traceState = parsedPayload.TraceState;
                if (!string.IsNullOrEmpty(parsedPayload.FaroSessionId))
                {
                    clientTags ??= [];
                    clientTags[BaggageToTagProcessor.SessionIdKey] = parsedPayload.FaroSessionId;
                }

                // Create a new invocation context with the tracing payload argument removed
                var originalArguments = invocationContext.HubMethodArguments.ToList();
                var argumentsForNext = originalArguments.Take(originalArguments.Count - 1).ToArray();
                if (argumentsForNext.Length == 0)
                {
                    // If the original method actually had no arguments, inject our placeholder argument.
                    argumentsForNext = [new PreventZeroArgumentHandler()];
                }
                contextToPass = new HubInvocationContext(
                    invocationContext.Context,
                    invocationContext.ServiceProvider,
                    invocationContext.Hub,
                    invocationContext.HubMethod,
                    argumentsForNext);
            }
        }

        var operationName = $"{invocationContext.HubMethod.DeclaringType.Name}/{invocationContext.HubMethodName}";

        if (!string.IsNullOrEmpty(traceParent))
        {
            return await _tracingService.ExecuteWithExternalParentTraceAsync(
                operationName,
                traceParent,
                traceState,
                async () => await next(contextToPass),
                clientTags);
        }
        else
        {
            // No external trace context found from client message arguments.
            // Create a new server span. It will be a root span if Activity.Current is null,
            // or a child of Activity.Current if it exists (e.g., from OnConnectedAsync).
            // clientTags will be null here if no tracingContext was found in args.
            return await _tracingService.ExecuteAsNewServerSpanAsync(operationName, async () => await next(contextToPass), clientTags);
        }
    }
}
