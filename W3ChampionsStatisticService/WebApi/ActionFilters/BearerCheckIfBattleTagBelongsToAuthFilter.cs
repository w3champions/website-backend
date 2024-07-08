using System;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

public class BearerCheckIfBattleTagBelongsToAuthFilter(IW3CAuthenticationService authService) : IAsyncActionFilter
{
    private readonly IW3CAuthenticationService _authService = authService;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        context.RouteData.Values.TryGetValue("battleTag", out var battleTag);
        var token = GetToken(context.HttpContext.Request.Headers[HeaderNames.Authorization]);
        try
        {
            var res = _authService.GetUserByToken(token, false);
            var btagString = battleTag?.ToString();

            if (!string.IsNullOrEmpty(btagString) && btagString.Equals(res.BattleTag))
            {
                context.ActionArguments["battleTag"] = res.BattleTag;
                await next.Invoke();
            }
        } catch (Exception)
        {
            var unauthorizedResult = new UnauthorizedObjectResult(new ErrorResult("Sorry H4ckerb0i"));
            context.Result = unauthorizedResult;
        }
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
