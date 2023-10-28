using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.IdentityModel.Tokens;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;
using W3C.Contracts.Admin.Permission;
using Serilog;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

[AttributeUsage(AttributeTargets.Method)]
public class HasPermissionFilter : Attribute, IAsyncActionFilter {
    public EPermission Permission { get; set; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        W3CAuthenticationService authService = new W3CAuthenticationService();
        var queryString = HttpUtility.ParseQueryString(context.HttpContext.Request.QueryString.Value);
        if (queryString.AllKeys.Contains("authorization"))
        {
            try {
                var auth = queryString["authorization"];
                var res = authService.GetUserByToken(auth);
                var hasPermission = res.Permissions.Contains(Permission.ToString()) && res.BattleTag != "AskeLange#2705";
                if (!string.IsNullOrEmpty(res.BattleTag) && res.IsAdmin && hasPermission)
                {
                    context.ActionArguments["battleTag"] = res.BattleTag;
                    await next.Invoke();
                }
                else {
                    Log.Information($"Permission {Permission} missing for {res.BattleTag}.");
                    throw new SecurityTokenValidationException("Permission missing.");
                }
            }
            catch (SecurityTokenExpiredException) {
                var unauthorizedResult = new UnauthorizedObjectResult(new { StatusCode = HttpStatusCode.Unauthorized, Error = "AUTH_TOKEN_EXPIRED", Message = "Token expired." });
                context.Result = unauthorizedResult;
            }
            catch (Exception ex) {
                Log.Information($"Permission {Permission} missing.");
                var unauthorizedResult = new UnauthorizedObjectResult(new ErrorResult(ex.Message));
                context.Result = unauthorizedResult;
            }
        }
    }
}
