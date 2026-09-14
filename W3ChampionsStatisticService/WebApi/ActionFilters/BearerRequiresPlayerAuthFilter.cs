using System;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
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
/// Lifetime is deliberately NOT validated (<c>validateLifetime: false</c>, spec §6.1), matching every
/// other non-admin path in this service (see <c>AuthSessionController.MintTicket</c>): an expired but
/// correctly signed player token is ACCEPTED. Players keep using old tokens and this is not an admin
/// surface — do not reuse this filter where token expiry must be enforced.
/// </para>
/// </summary>
public class BearerRequiresPlayerAuthFilter(
    IW3CAuthenticationService authService,
    ILogger<BearerRequiresPlayerAuthFilter> logger) : IAsyncAuthorizationFilter
{
    /// <summary>Key under which the validated battleTag is published to the action.</summary>
    public const string BattleTagItemKey = "battleTag";

    private readonly IW3CAuthenticationService _authService = authService;
    private readonly ILogger<BearerRequiresPlayerAuthFilter> _logger = logger;

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

        // "Authorization: Bearer" without a credential parses to a null token: reject it here rather than
        // rely on the service throwing for it.
        if (string.IsNullOrEmpty(token))
        {
            return Deny(context);
        }

        W3CUserAuthenticationDto identity;
        try
        {
            // Throws for a malformed token or an invalid signature. An expired but correctly signed token
            // does NOT throw, because lifetime is not validated here (see the class summary).
            identity = _authService.GetUserByToken(token, false);
        }
        catch (Exception ex) when (IsTokenRejection(ex))
        {
            // The client's token is unusable: expected, client-controlled noise, so nothing is logged.
            return Deny(context);
        }
        catch (Exception ex)
        {
            // Anything else is a server-side fault that would otherwise look exactly like a bad token, e.g. a
            // claim-shape change (InvalidOperationException, FormatException) or an unreadable JWT_PUBLIC_KEY
            // (ArgumentException from the key import). Log the exception TYPE only: never the token, and never
            // the message, which can quote it.
            _logger.LogWarning(
                "Player JWT could not be verified for a reason other than a rejected token: {ExceptionType}",
                ex.GetType().FullName);
            return Deny(context);
        }

        // The real service throws rather than returning null; the guard keeps this fail-closed regardless.
        if (identity == null || string.IsNullOrEmpty(identity.BattleTag))
        {
            return Deny(context);
        }

        context.HttpContext.Items[BattleTagItemKey] = identity.BattleTag;
        return Task.CompletedTask;
    }

    /// <summary>
    /// True when IdentityModel rejected the client's token itself. Verified against
    /// System.IdentityModel.Tokens.Jwt 8.10.0:
    /// <list type="bullet">
    /// <item>bad signature, unknown signing key or <c>alg: none</c>: <see cref="SecurityTokenException"/>
    /// subclasses (<see cref="SecurityTokenInvalidSignatureException"/>,
    /// <see cref="SecurityTokenSignatureKeyNotFoundException"/>);</item>
    /// <item>not a JWT, or segments that are not base64url JSON: <see cref="SecurityTokenMalformedException"/>
    /// or a plain <see cref="ArgumentException"/> (IDX12729). Neither is a <see cref="SecurityTokenException"/>,
    /// and the plain type is shared with <c>RSA.ImportFromPem</c> rejecting a broken public key, so an
    /// <see cref="ArgumentException"/> counts only when the JWT handler's own assembly threw it.</item>
    /// </list>
    /// </summary>
    private static bool IsTokenRejection(Exception ex) =>
        ex is SecurityTokenException
        || (ex is ArgumentException && ex.TargetSite?.DeclaringType?.Assembly == typeof(JwtSecurityTokenHandler).Assembly);

    private static Task Deny(AuthorizationFilterContext context)
    {
        context.Result = new UnauthorizedObjectResult(new ErrorResult("Invalid token"));
        return Task.CompletedTask;
    }
}
