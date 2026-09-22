using System;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// Stops MVC from buffering the multipart body into a form collection while it binds the action's arguments, so the
/// action can read <c>Request.Body</c> itself — the temporary upload with a <c>MultipartReader</c>, the admin map-file
/// passthrough as the stream it forwards. Without it, an action with any bindable parameter has its multipart body
/// read in full (and buffered) before it runs.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var factories = context.ValueProviderFactories;
        for (var i = factories.Count - 1; i >= 0; i--)
        {
            if (factories[i] is FormValueProviderFactory or FormFileValueProviderFactory or JQueryFormValueProviderFactory)
            {
                factories.RemoveAt(i);
            }
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
