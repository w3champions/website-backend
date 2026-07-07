using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Sessions;

/// <summary>
/// Secure ticket-mint endpoint (WB-1). Lets the launcher authenticate to the WebsiteBackendHub
/// SignalR hub WITHOUT ever handing a raw long-lived JWT to JavaScript / a query string: the JWT is
/// exchanged once (over an Authorization header) for a short-lived single-use ticket, and only the
/// ticket rides the WebSocket URL. Mirrors the already-shipped chat-service AuthSessionController.
///
/// ===== CLIENT CONTRACT (pinned — launcher LE-2 builds against this exact shape) =====
///   Verb / path : POST /auth/session
///   Auth header : Authorization: Bearer &lt;W3C JWT&gt;   (signature + expiry enforced)
///   Request body: EMPTY
///   200 OK      : { "ticket": "&lt;64 hex chars&gt;", "expiresInSeconds": 60 }
///   401         : missing / non-Bearer / invalid / expired JWT
///   429         : per-battleTag mint rate limit exceeded (10 / minute)
///   Ticket      : single-use, 60s TTL. The client hands it to SignalR's accessTokenFactory so it
///                 arrives as ?access_token=&lt;ticket&gt; on /websiteBackendHub, where
///                 WebsiteBackendHub.OnConnectedAsync consumes it exactly once. That hub is
///                 TICKET-ONLY (no raw-JWT fallback): the launcher is its sole client, browsers
///                 use REST + Bearer and never open the WebSocket, and the cutover is lock-step
///                 with the forced launcher update.
/// ====================================================================================
/// </summary>
[ApiController]
[Route("auth")]
public class AuthSessionController(
    IW3CAuthenticationService authService,
    ITicketStore ticketStore,
    MintRateLimiter rateLimiter) : ControllerBase
{
    private readonly IW3CAuthenticationService _authService = authService;
    private readonly ITicketStore _ticketStore = ticketStore;
    private readonly MintRateLimiter _rateLimiter = rateLimiter;

    [HttpPost("session")]
    public IActionResult MintTicket()
    {
        var now = DateTime.UtcNow;

        // Parse the Bearer token first. GetToken throws SecurityTokenValidationException on a
        // missing / non-Bearer header.
        string token;
        try
        {
            token = BearerHasPermissionFilter.GetToken(Request.Headers[HeaderNames.Authorization]);
        }
        catch (SecurityTokenValidationException)
        {
            return Unauthorized();
        }

        // Validate signature + lifetime. GetUserByToken THROWS on bad/expired/garbage tokens (it never
        // returns null) — treat any failure as 401. No ticket is minted for an unvalidated caller.
        W3CUserAuthenticationDto identity;
        try
        {
            identity = _authService.GetUserByToken(token, validateLifetime: true);
        }
        catch (Exception)
        {
            return Unauthorized();
        }

        if (identity == null || string.IsNullOrEmpty(identity.BattleTag))
        {
            return Unauthorized();
        }

        // Rate limit PER-BATTLETAG ONLY — deliberately NO per-IP limiter (unlike chat-service).
        // RATIONALE (load-bearing; read before adding an IP bucket): wb sits behind the shared W3C
        // reverse-proxy chain (russia-gateway -> Traefik passthrough -> nginx-proxy). Program.cs DOES
        // call UseForwardedHeaders, but it only trusts the Docker network (172.18.0.0/16) and the
        // russia-gateway (212.60.5.180) as known proxies, at the default ForwardLimit — so with the
        // real multi-hop passthrough topology HttpContext.Connection.RemoteIpAddress is NOT a reliable
        // per-client value: it collapses toward a shared proxy IP for the whole fleet (a per-IP limit
        // would then throttle production globally), and where an XFF hop IS honored it is
        // client-spoofable. Keying on the cryptographically-validated JWT battleTag is the correct
        // abuse control here. Unauthenticated request floods are an infra/proxy-layer concern.
        if (!_rateLimiter.TryAcquire($"bt:{identity.BattleTag}", SessionLimits.TicketMintPerBattleTagLimit, now))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        return Ok(new TicketResponse
        {
            Ticket = _ticketStore.Mint(identity, now),
            ExpiresInSeconds = SessionLimits.TicketTtlSeconds,
        });
    }
}

public class TicketResponse
{
    public string Ticket { get; set; }
    public int ExpiresInSeconds { get; set; }
}
