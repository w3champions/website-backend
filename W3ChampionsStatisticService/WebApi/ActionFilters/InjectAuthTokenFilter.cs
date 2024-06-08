using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

public class InjectAuthTokenFilter : IAsyncActionFilter {
    
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        context.ActionArguments["authToken"] = GetToken(context.HttpContext.Request.Headers[HeaderNames.Authorization]);
        await next.Invoke();
    }
    
    private static string GetToken(StringValues authorization)
    {
        if (AuthenticationHeaderValue.TryParse(authorization, out var headerValue))
        {
            if (headerValue.Scheme == "Bearer")
            {
                return headerValue.Parameter;
            }
        }
        return "";
    }
}
