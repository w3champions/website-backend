using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.IdentityModel.Tokens;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

public class CheckIfBattleTagIsAdminFilter : IAsyncActionFilter {
    private readonly IW3CAuthenticationService _authService;

    public CheckIfBattleTagIsAdminFilter(IW3CAuthenticationService authService)
    {
        _authService = authService;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var queryString = HttpUtility.ParseQueryString(context.HttpContext.Request.QueryString.Value);
        if (queryString.AllKeys.Contains("authorization"))
        {
            try {
                var auth = queryString["authorization"];
                var res = _authService.GetUserByToken(auth);
                if (!string.IsNullOrEmpty(res.BattleTag) && res.IsAdmin)
                {
                    context.ActionArguments["battleTag"] = res.BattleTag;
                    await next.Invoke();
                }
            }
            catch (SecurityTokenExpiredException) {
                var unauthorizedResult = new UnauthorizedObjectResult(new { StatusCode = HttpStatusCode.Unauthorized, Error = "AUTH_TOKEN_EXPIRED", Message = "Token expired." });
                context.Result = unauthorizedResult;
            }
            catch (Exception) {
                var unauthorizedResult = new UnauthorizedObjectResult(new ErrorResult("Sorry H4ckerb0i"));
                context.Result = unauthorizedResult;
            }
        }
    }
}
