using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;

namespace W3ChampionsStatisticService.Services;

public class TracingService(ActivitySource activitySource, IHttpContextAccessor httpContextAccessor)
{
    private readonly ActivitySource _activitySource = activitySource;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    // Optional: If you need to manually start an activity and manage its lifecycle elsewhere.
    public Activity StartActivity(
        object classInstance, 
        IEnumerable<KeyValuePair<string, object>> tags = null,
        [CallerMemberName] string methodName = "")
    {
        string fullSpanName = $"{classInstance.GetType().Name}.{methodName}";
        var activity = _activitySource.StartActivity(fullSpanName, ActivityKind.Internal);
        if (activity != null && tags != null)
        {
            foreach (var tag in tags)
            {
                activity.SetTag(tag.Key, tag.Value);
            }
        }
        return activity;
    }

    public void ExecuteWithSpan(
        object classInstance,
        Action action,
        IEnumerable<KeyValuePair<string, object>> tags = null,
        bool forceNewRoot = false,
        [CallerMemberName] string methodName = "")
    {
        ExecuteWithActivityInternal<object>(classInstance, () => { action(); return null; }, tags, forceNewRoot, methodName);
    }

    public T ExecuteWithSpan<T>(
        object classInstance,
        Func<T> func,
        IEnumerable<KeyValuePair<string, object>> tags = null,
        bool forceNewRoot = false,
        [CallerMemberName] string methodName = "")
    {
        return ExecuteWithActivityInternal(classInstance, func, tags, forceNewRoot, methodName);
    }

    public async Task ExecuteWithSpanAsync(
        object classInstance,
        Func<Task> funcAsync,
        IEnumerable<KeyValuePair<string, object>> tags = null,
        bool forceNewRoot = false,
        [CallerMemberName] string methodName = "")
    {
        await ExecuteWithActivityInternalAsync<object>(classInstance, async () => { await funcAsync(); return null; }, tags, forceNewRoot, methodName);
    }

    public async Task<T> ExecuteWithSpanAsync<T>(
        object classInstance,
        Func<Task<T>> funcAsync,
        IEnumerable<KeyValuePair<string, object>> tags = null,
        bool forceNewRoot = false,
        [CallerMemberName] string methodName = "")
    {
        return await ExecuteWithActivityInternalAsync(classInstance, funcAsync, tags, forceNewRoot, methodName);
    }

    private void ApplyTags(Activity activity, IEnumerable<KeyValuePair<string, object>> tags)
    {
        if (activity != null && tags != null)
        {
            foreach (var tag in tags)
            {
                activity.SetTag(tag.Key, tag.Value);
            }
        }
    }

    private TResult ExecuteWithActivityInternal<TResult>(
        object classInstance,
        Func<TResult> operation,
        IEnumerable<KeyValuePair<string, object>> tags,
        bool forceNewRoot,
        string methodName)
    {
        string fullSpanName = $"{classInstance.GetType().Name}.{methodName}";
        Activity createdActivity;
        if (forceNewRoot)
        {
            createdActivity = _activitySource.StartActivity(fullSpanName, ActivityKind.Internal, parentContext: default);
        }
        else
        {
            createdActivity = _activitySource.StartActivity(fullSpanName, ActivityKind.Internal);
        }

        using var activity = createdActivity;
        ApplyTags(activity, tags);

        try
        {
            return operation();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.ToString());
            throw;
        }
    }

    private async Task<TResult> ExecuteWithActivityInternalAsync<TResult>(
        object classInstance,
        Func<Task<TResult>> operationAsync,
        IEnumerable<KeyValuePair<string, object>> tags,
        bool forceNewRoot,
        string methodName)
    {
        string fullSpanName = $"{classInstance.GetType().Name}.{methodName}";
        Activity createdActivity;
        if (forceNewRoot)
        {
            createdActivity = _activitySource.StartActivity(fullSpanName, ActivityKind.Internal, parentContext: default);
        }
        else
        {
            createdActivity = _activitySource.StartActivity(fullSpanName, ActivityKind.Internal);
        }

        using var activity = createdActivity;
        ApplyTags(activity, tags);

        try
        {
            return await operationAsync();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.ToString());
            throw;
        }
    }

    public async Task<T> ExecuteWithExternalParentTraceAsync<T>(string name, string traceParent, string traceState, Func<Task<T>> action, Dictionary<string, object> clientTags = null)
    {
        ActivityContext parentContext = default;
        if (!string.IsNullOrEmpty(traceParent))
        {
            if (ActivityContext.TryParse(traceParent, traceState, out var context))
            {
                parentContext = context;
            }
        }

        using var activity = _activitySource.StartActivity(name, ActivityKind.Server, parentContext);
        ApplyServerSpanTags(activity, clientTags);

        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            RecordOtelError(activity, ex);
            throw;
        }
    }

    public async Task<T> ExecuteAsNewServerSpanAsync<T>(string name, Func<Task<T>> action, Dictionary<string, object> tags = null)
    {
        // If Activity.Current is null, this becomes a root. Otherwise, a child.
        // The ActivityKind.Server indicates this span represents a server-side operation typically initiated by a remote request.
        using var activity = _activitySource.StartActivity(name, ActivityKind.Server); // Parent context is implicitly Activity.Current
        ApplyServerSpanTags(activity, tags);

        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            RecordOtelError(activity, ex);
            throw;
        }
    }

    private void ApplyServerSpanTags(Activity activity, IDictionary<string, object> tags)
    {
        if (activity != null && tags != null)
        {
            foreach (var tag in tags)
            {
                activity.SetTag(tag.Key, tag.Value);

                // Add specific tags to baggage for propagation
                if (tag.Key == BaggageToTagProcessor.SessionIdKey && tag.Value is string sessionId && !string.IsNullOrEmpty(sessionId))
                {
                    activity.AddBaggage(BaggageToTagProcessor.SessionIdKey, sessionId);
                }
                else if (tag.Key == BaggageToTagProcessor.BattleTagKey && tag.Value is string battleTag && !string.IsNullOrEmpty(battleTag))
                {
                    activity.AddBaggage(BaggageToTagProcessor.BattleTagKey, battleTag);
                }
            }
        }
    }

    private void RecordOtelError(Activity activity, Exception ex)
    {
        if (activity == null) return;
        activity.SetTag("otel.status_code", "ERROR");
        activity.SetTag("otel.status_description", ex.Message);
        activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection {
            { "exception.type", ex.GetType().FullName },
            { "exception.message", ex.Message },
            { "exception.stacktrace", ex.StackTrace }
        }));
    }
} 