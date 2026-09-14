using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

/// <summary>
/// Authenticates a plain (non-admin) player JWT for player-facing endpoints that read the request
/// body themselves.
/// <para>
/// This is an <see cref="IAsyncAuthorizationFilter"/> — NOT an <c>IAsyncActionFilter</c> like every
/// other auth filter in this service — and that is load-bearing: authorization filters run before
/// resource filters, before model binding and before the action, so a 401 is written without a
/// single byte of a (potentially 256 MiB) request body being consumed. An action filter would run
/// after model binding and lose that property. wb registers no JWT bearer scheme
/// (<c>WebApi/Authorization/BasicAuthConfiguration.cs</c>), so <c>[Authorize]</c> is unavailable.
/// </para>
/// <para>
/// Lifetime is deliberately NOT validated (<c>validateLifetime: false</c>), matching every other
/// non-admin path in this service (see <c>AuthSessionController.MintTicket</c>): players keep using
/// old tokens and this is not an admin surface.
/// </para>
/// </summary>
public class BearerRequiresPlayerAuthFilter(IW3CAuthenticationService authService) : IAsyncAuthorizationFilter
{
    /// <summary>Key under which the validated battleTag is published to the action.</summary>
    public const string BattleTagItemKey = "battleTag";

    private readonly IW3CAuthenticationService _authService = authService;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        string token;
        try
        {
            token = BearerHasPermissionFilter.GetToken(context.HttpContext.Request.Headers[HeaderNames.Authorization]);
        }
        catch (SecurityTokenValidationException)
        {
            return Deny(context);
        }

        W3CUserAuthenticationDto identity;
        try
        {
            // Throws on bad/garbage/expired-signature tokens; it never returns null.
            identity = _authService.GetUserByToken(token, false);
        }
        catch (Exception)
        {
            return Deny(context);
        }

        if (identity == null || string.IsNullOrEmpty(identity.BattleTag))
        {
            return Deny(context);
        }

        context.HttpContext.Items[BattleTagItemKey] = identity.BattleTag;
        return Task.CompletedTask;
    }

    private static Task Deny(AuthorizationFilterContext context)
    {
        context.Result = new UnauthorizedObjectResult(new ErrorResult("Invalid token"));
        return Task.CompletedTask;
    }
}
