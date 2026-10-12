using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

/// <summary>
/// Any signed-in user, with a token that has not expired. Sets the action argument <c>battleTag</c> to the
/// user of the token, as <see cref="BearerHasPermissionFilter"/> does for admins.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class BearerSignedInAttribute : Attribute, IAsyncActionFilter
{
    private EPermission? _onlyWithPermission;

    /// <summary>
    /// For a feature that is not open to all users yet: only admins with this permission (else 403).
    /// Not set: any signed-in user.
    /// </summary>
    public EPermission OnlyWithPermission
    {
        get => _onlyWithPermission ?? default;
        set => _onlyWithPermission = value;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        W3CUserAuthenticationDto user;
        try
        {
            var token = BearerHasPermissionFilter.GetToken(context.HttpContext.Request.Headers[HeaderNames.Authorization]);
            user = context.HttpContext.RequestServices.GetRequiredService<IW3CAuthenticationService>().GetUserByToken(token, true);
        }
        catch (SecurityTokenExpiredException)
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Error = "AUTH_TOKEN_EXPIRED",
                Message = "Token expired.",
            });
            return;
        }
        catch (Exception)
        {
            user = null;
        }

        if (string.IsNullOrEmpty(user?.BattleTag))
        {
            context.Result = new UnauthorizedObjectResult(new ErrorResult("Sign in to continue."));
            return;
        }

        if (_onlyWithPermission is { } permission && !(user.IsAdmin && user.Permissions?.Contains(permission) == true))
        {
            context.Result = new ObjectResult(new ErrorResult("This page is not open yet.")) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        context.ActionArguments["battleTag"] = user.BattleTag;
        await next();
    }
}
