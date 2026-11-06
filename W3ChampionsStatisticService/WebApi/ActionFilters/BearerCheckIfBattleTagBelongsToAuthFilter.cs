using System;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
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

        W3CUserAuthenticationDto user;
        try
        {
            var token = GetToken(context.HttpContext.Request.Headers[HeaderNames.Authorization]);
            user = _authService.GetUserByToken(token, false);
        }
        catch (Exception)
        {
            context.Result = new UnauthorizedObjectResult(new ErrorResult("Sorry H4ckerb0i"));
            return;
        }

        var btagString = battleTag?.ToString();
        if (string.IsNullOrEmpty(btagString) || !btagString.Equals(user.BattleTag))
        {
            context.Result = new ObjectResult(new ErrorResult("battleTag does not match the authenticated user"))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        context.ActionArguments["battleTag"] = user.BattleTag;
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
        throw new SecurityTokenValidationException("Invalid token");
    }
}
