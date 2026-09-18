using System;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;
using W3C.Domain.Tracing;
namespace W3ChampionsStatisticService.WebApi.ActionFilters;

public class BearerCheckIfBattleTagBelongsToAuthFilter(IW3CAuthenticationService authService) : IAsyncActionFilter
{
    private readonly IW3CAuthenticationService _authService = authService;

    [Trace]
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        context.RouteData.Values.TryGetValue("battleTag", out var battleTag);
        try
        {
            // Inside the try: a missing or non-Bearer Authorization header throws here and must be a 401,
            // not an unhandled exception (500).
            var token = GetToken(context.HttpContext.Request.Headers[HeaderNames.Authorization]);
            var res = _authService.GetUserByToken(token, false);
            var btagString = battleTag?.ToString();

            if (!string.IsNullOrEmpty(btagString) && btagString.Equals(res.BattleTag))
            {
                context.ActionArguments["battleTag"] = res.BattleTag;
                await next.Invoke();
            }
            else
            {
                // Without a Result, MVC treats the un-invoked pipeline as short-circuited and writes an
                // empty 200.
                context.Result = InvalidAuthResult();
            }
        }
        catch (Exception)
        {
            context.Result = InvalidAuthResult();
        }
    }

    private static UnauthorizedObjectResult InvalidAuthResult() => new(new ErrorResult("Sorry H4ckerb0i"));

    private static string GetToken(StringValues authorization)
    {
        if (AuthenticationHeaderValue.TryParse(authorization, out var headerValue))
        {
            if (headerValue.Scheme == "Bearer")
            {
                return headerValue.Parameter;
            }
        }
        throw new SecurityTokenValidationException("Invalid token");
    }
}
