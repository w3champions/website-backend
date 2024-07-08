using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

public class InjectActingPlayerFromAuthCodeFilter(IW3CAuthenticationService authService) : IAsyncActionFilter {
    private readonly IW3CAuthenticationService _authService = authService;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var token = GetToken(context.HttpContext.Request.Headers[HeaderNames.Authorization]);
        if (token != null)
        {
            var res = _authService.GetUserByToken(token);
            var actingPlayerContent = context.ActionDescriptor.Parameters.FirstOrDefault(a => a.Name == "actingPlayer");
            if (actingPlayerContent != null)
            {
                context.ActionArguments["actingPlayer"] = res.BattleTag;
                await next.Invoke();
            }
        }

        var unauthorizedResult = new UnauthorizedObjectResult(new ErrorResult("Sorry H4ckerb0i"));
        context.Result = unauthorizedResult;
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
        return null;
    }
}
