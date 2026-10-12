using System;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace W3ChampionsStatisticService.NetworkDiagnostics.Api;

/// <summary>
/// Keeps MVC from reading the request body as a form. For a form content type MVC otherwise reads the whole body
/// while it prepares model binding - whatever the parameters' binding sources are, and before any action filter
/// (such as the player authentication) has run. A resource filter runs before that.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var factories = context.ValueProviderFactories;
        factories.RemoveType<FormValueProviderFactory>();
        factories.RemoveType<FormFileValueProviderFactory>();
        factories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
