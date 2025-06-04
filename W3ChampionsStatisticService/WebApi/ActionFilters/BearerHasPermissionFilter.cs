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
using W3C.Contracts.Admin.Permission;
using Serilog;
using W3C.Domain.Tracing;
namespace W3ChampionsStatisticService.WebApi.ActionFilters;

[AttributeUsage(AttributeTargets.Method)]
public class BearerHasPermissionFilter : Attribute, IAsyncActionFilter
{
    public EPermission Permission { get; set; }

    [Trace]
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        W3CAuthenticationService authService = new();
        {
            try
            {
                var token = GetToken(context.HttpContext.Request.Headers[HeaderNames.Authorization]);
                var res = authService.GetUserByToken(token, true);
                var hasPermission = res.Permissions.Contains(Permission);
                if (!string.IsNullOrEmpty(res.BattleTag) && res.IsAdmin && hasPermission)
                {
                    context.ActionArguments["battleTag"] = res.BattleTag;
                    await next.Invoke();
                }
                else
                {
                    Log.Information($"Permission {Permission} missing for {res.BattleTag}.");
                    throw new SecurityTokenValidationException("Permission missing.");
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
            catch (Exception ex)
            {
                Log.Information($"Permission {Permission} missing.");
                var unauthorizedResult = new UnauthorizedObjectResult(new ErrorResult(ex.Message));
                context.Result = unauthorizedResult;
            }
        }
    }

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
}
