using System;
using System.Net;
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

public class CheckIfBattleTagIsAdminFilter(IW3CAuthenticationService authService) : IAsyncActionFilter
{
    private readonly IW3CAuthenticationService _authService = authService;

    [Trace]
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try
        {
            string token = GetToken(context.HttpContext.Request.Headers[HeaderNames.Authorization]);
            W3CUserAuthenticationDto res = _authService.GetUserByToken(token, true);
            if (!string.IsNullOrEmpty(res.BattleTag) && res.IsAdmin && !IsRevoked(res))
            {
                context.ActionArguments["battleTag"] = res.BattleTag;
                await next.Invoke();
            }
            else
            {
                // A valid token without admin rights deliberately gets an empty 200; the action still never runs.
                // The website's admin JWT-lifetime check logs the user out on any non-2xx from this filter, so a
                // 401 or 403 here would log out every non-admin who opens the admin page. Switching to 403
                // requires the website to run that check only for admins first. EmptyResult is what MVC
                // substitutes when a filter short-circuits without setting a Result.
                context.Result = new EmptyResult();
            }
        }
        catch (SecurityTokenExpiredException)
        {
            var unauthorizedResult = new UnauthorizedObjectResult(new
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Error = "AUTH_TOKEN_EXPIRED",
                Message = "Token expired."
            });
            context.Result = unauthorizedResult;
        }
        catch (Exception)
        {
            context.Result = InvalidAuthResult();
        }
    }

    private static UnauthorizedObjectResult InvalidAuthResult() => new(new ErrorResult("Sorry H4ckerb0i"));

    public static string GetToken(StringValues authorization)
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

    public static bool IsRevoked(W3CUserAuthenticationDto userAuthDto)
    {
        return userAuthDto.BattleTag == "Footman#21819";
    }
}
