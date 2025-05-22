using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using W3C.Domain.Tracing;
namespace W3ChampionsStatisticService.Services.Interceptors;

public class TracingInterceptor(ActivitySource activitySource) : IInterceptor
{
    private readonly ActivitySource _activitySource = activitySource;

    public void Intercept(IInvocation invocation)
    {
        // 1. Check for [NoTrace] on the method first
        var noTraceAttributeOnMethod = invocation.MethodInvocationTarget.GetCustomAttribute<NoTraceAttribute>()
                                     ?? invocation.Method.GetCustomAttribute<NoTraceAttribute>();

        if (noTraceAttributeOnMethod != null)
        {
            invocation.Proceed(); // [NoTrace] found, proceed without tracing.
            return;
        }

        // 2. Check for [Trace] on the method
        var methodTraceAttribute = invocation.MethodInvocationTarget.GetCustomAttribute<TraceAttribute>()
                                 ?? invocation.Method.GetCustomAttribute<TraceAttribute>();

        TraceAttribute effectiveTraceAttribute = methodTraceAttribute; // Prioritize method attribute

        // 3. If no [Trace] on method, check for [Trace] on the class (TargetType)
        if (effectiveTraceAttribute == null)
        {
            effectiveTraceAttribute = invocation.TargetType.GetCustomAttribute<TraceAttribute>();
        }

        if (effectiveTraceAttribute == null)
        {
            invocation.Proceed(); // No [Trace] attribute on method or class, proceed without tracing.
            return;
        }

        string className = invocation.TargetType.Name;
        string methodName;

        if (methodTraceAttribute != null)
        {
            // [Trace] is on the method, OperationName is correctly set by [CallerMemberName]
            methodName = methodTraceAttribute.OperationName;
        }
        else
        {
            // [Trace] is on the class, so use the actual invoked method's name for the span's method part.
            // effectiveTraceAttribute.OperationName (from class) is ignored here for the method part of the span name.
            methodName = invocation.Method.Name;
        }

        string fullSpanName = $"{className}.{methodName}";

        using var activity = _activitySource.StartActivity(fullSpanName, ActivityKind.Internal);

        try
        {
            if (activity != null)
            {
                ParameterInfo[] parameters = invocation.Method.GetParameters();
                for (int i = 0; i < invocation.Arguments.Length; i++)
                {
                    // Check if the parameter has [NoTrace]
                    if (parameters[i].GetCustomAttribute<NoTraceAttribute>() == null)
                    {
                        var arg = invocation.Arguments[i];
                        var paramName = parameters[i].Name;
                        activity.SetTag($"param.{paramName}", arg?.ToString() ?? "null");
                    }
                }
            }

            invocation.Proceed();

            if (invocation.ReturnValue is Task taskResult)
            {
                taskResult.ContinueWith(task =>
                {
                    if (task.Exception != null)
                    {
                        activity?.SetStatus(ActivityStatusCode.Error, task.Exception.InnerException?.ToString() ?? task.Exception.ToString());
                    }
                }, TaskScheduler.Default);
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.ToString());
            throw;
        }
    }
}
