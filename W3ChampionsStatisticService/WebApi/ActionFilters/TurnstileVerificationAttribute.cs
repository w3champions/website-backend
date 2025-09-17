using System;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class TurnstileVerificationAttribute : Attribute, IFilterFactory
{
    /// <summary>
    /// Optional: Maximum age of the token in seconds. 
    /// If specified (value > 0), tokens older than this will be rejected.
    /// Set to 0 or negative to disable age check.
    /// </summary>
    public int MaxAgeSeconds { get; set; }
    
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        var filter = serviceProvider.GetRequiredService<TurnstileVerificationFilter>();
        filter.MaxAgeSeconds = MaxAgeSeconds;
        return filter;
    }
}
