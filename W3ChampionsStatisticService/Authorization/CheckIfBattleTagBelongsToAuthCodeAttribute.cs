using System;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace W3ChampionsStatisticService.Authorization
{
    [AttributeUsage(AttributeTargets.Method)]
    public class CheckIfBattleTagBelongsToAuthCodeAttribute : Attribute, IFilterFactory
    {
        public bool IsReusable => false;

        public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        {
            return serviceProvider.GetService<CheckIfBattleTagBelongsToAuthCodeFilter>();
        }
    }
}