using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using System;
using System.Threading.Tasks;

namespace W3ChampionsStatisticService.WebApi.ActionFilters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class CheckIfInternalServiceAttribute : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var internalSecret = Environment.GetEnvironmentVariable("INTERNAL_API_SECRET") ?? "default-secret";
            
            context.HttpContext.Request.Headers.TryGetValue("X-Internal-Secret", out StringValues providedSecret);

            if (internalSecret == providedSecret)
            {
                await next(); // Authorized, proceed
            }
            else
            {
                context.Result = new UnauthorizedObjectResult("Invalid internal service secret.");
            }
        }
    }
} 