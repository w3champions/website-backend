using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

[AttributeUsage(AttributeTargets.Method)]
public class InjectActingPlayerAuthCodeAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetService<InjectActingPlayerFromAuthCodeFilter>();
    }

    public static W3CUserAuthenticationDto GetActingPlayerUser(HttpContext context)
    {
        if (context.Items.TryGetValue(InjectActingPlayerFromAuthCodeFilter.ActingPlayerUserKey, out var value))
        {
            return value as W3CUserAuthenticationDto;
        }
        throw new Exception("No acting player user found in context");
    }
}
