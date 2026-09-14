using System;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

/// <summary>
/// DI-resolving marker for <see cref="BearerRequiresPlayerAuthFilter"/>, mirroring
/// <c>BearerCheckIfBattleTagBelongsToAuthAttribute</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class BearerRequiresPlayerAuthAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        => serviceProvider.GetService<BearerRequiresPlayerAuthFilter>();
}
