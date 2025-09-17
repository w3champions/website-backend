using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

public class TurnstileVerificationFilter(ITurnstileService turnstileService, ILogger<TurnstileVerificationFilter> logger) : IAsyncActionFilter
{
    private readonly ITurnstileService _turnstileService = turnstileService;
    private readonly ILogger<TurnstileVerificationFilter> _logger = logger;
    private const string TURNSTILE_HEADER = "X-Turnstile-Token";

    /// <summary>
    /// Optional: Maximum age of the token in seconds. Set by the attribute.
    /// Value of 0 or negative means no age check.
    /// </summary>
    public int MaxAgeSeconds { get; set; }

    [Trace]
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Skip if Turnstile is not enabled
        if (!_turnstileService.IsEnabled)
        {
            await next.Invoke();
            return;
        }

        var endpoint = context.HttpContext.Request.Path;
        var method = context.HttpContext.Request.Method;
        var userAgent = context.HttpContext.Request.Headers["User-Agent"].ToString();
        var remoteIp = GetRemoteIpAddress(context);

        // Extract token from header
        if (!context.HttpContext.Request.Headers.TryGetValue(TURNSTILE_HEADER, out var tokenValue))
        {
            _logger.LogWarning("Turnstile token missing from request. Endpoint: {Method} {Endpoint}, IP: {RemoteIp}, User-Agent: {UserAgent}",
                method, endpoint, remoteIp, userAgent);
            context.Result = new UnauthorizedObjectResult(new
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Error = "TURNSTILE_TOKEN_MISSING",
                Message = "Turnstile verification token is required"
            });
            return;
        }

        var token = tokenValue.ToString();

        try
        {
            var result = await _turnstileService.VerifyTokenAsync(token, remoteIp, MaxAgeSeconds > 0 ? MaxAgeSeconds : null);

            if (!result.IsValid)
            {
                if (result.IsExpiredByAge)
                {
                    _logger.LogInformation("Turnstile token expired by age. Endpoint: {Method} {Endpoint}, IP: {RemoteIp}, User-Agent: {UserAgent}, MaxAge: {MaxAge}s",
                        method, endpoint, remoteIp, userAgent, MaxAgeSeconds);
                }
                else
                {
                    _logger.LogInformation("Invalid Turnstile token. Endpoint: {Method} {Endpoint}, IP: {RemoteIp}, User-Agent: {UserAgent}",
                        method, endpoint, remoteIp, userAgent);
                }

                context.Result = new UnauthorizedObjectResult(new
                {
                    StatusCode = HttpStatusCode.Unauthorized,
                    Error = result.IsExpiredByAge ? "TURNSTILE_TOKEN_EXPIRED" : "TURNSTILE_VERIFICATION_FAILED",
                    Message = result.ErrorMessage ?? "Turnstile verification failed. Please refresh and try again."
                });
                return;
            }

            _logger.LogDebug("Turnstile verification successful. Endpoint: {Method} {Endpoint}, IP: {RemoteIp}",
                method, endpoint, remoteIp);

            // Token is valid, proceed with the action
            await next.Invoke();
        }
        catch (TurnstileVerificationException ex)
        {
            _logger.LogError(ex, "Turnstile verification service error. Endpoint: {Method} {Endpoint}, IP: {RemoteIp}, User-Agent: {UserAgent}",
                method, endpoint, remoteIp, userAgent);
            context.Result = new ObjectResult(new
            {
                StatusCode = HttpStatusCode.ServiceUnavailable,
                Error = "TURNSTILE_SERVICE_ERROR",
                Message = "Unable to verify captcha at this time. Please try again later."
            })
            {
                StatusCode = (int)HttpStatusCode.ServiceUnavailable
            };
        }
    }

    private string GetRemoteIpAddress(ActionExecutingContext context)
    {
        // Check for Cloudflare's CF-Connecting-IP header first
        if (context.HttpContext.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp))
        {
            return cfIp.ToString();
        }

        // Then check X-Forwarded-For
        if (context.HttpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            // X-Forwarded-For can contain multiple IPs, take the first one
            var ips = forwardedFor.ToString().Split(',');
            if (ips.Length > 0)
            {
                return ips[0].Trim();
            }
        }

        // Fall back to remote IP address
        return context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    }
}
