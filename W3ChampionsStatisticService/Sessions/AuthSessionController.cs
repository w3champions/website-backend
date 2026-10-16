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
/// exchanged once (over an Authorization header) for a short-lived ticket, and only the ticket rides
/// the WebSocket URL. Mirrors the already-shipped chat-service AuthSessionController.
///
/// ===== CLIENT CONTRACT (pinned — launcher LE-2 builds against this exact shape) =====
///   Verb / path : POST /auth/session
///   Auth header : Authorization: Bearer &lt;W3C JWT&gt;   (signature enforced, expiry is not)
///   Request body: EMPTY
///   200 OK      : { "ticket": "&lt;64 hex chars&gt;", "expiresInSeconds": 60, "maxRedemptions": 5 }
///   401         : missing / non-Bearer / invalid JWT
///   429         : per-battleTag mint rate limit exceeded (10 / minute)
///   Ticket      : 60s TTL from mint. The client hands it to SignalR's accessTokenFactory so it
///                 arrives as ?access_token=&lt;ticket&gt; on /websiteBackendHub, where
///                 WebsiteBackendHub.OnConnectedAsync redeems it. A ticket can be presented up to
///                 maxRedemptions times inside its TTL, one connection at a time (rules on
///                 TicketStore), so a reconnecting launcher can reuse it. Launchers that predate
///                 maxRedemptions mint per attempt, which still works. The hub is
///                 ticket-only: a raw JWT in access_token is rejected (AuthorizationFailed +
///                 abort), like chat-service's ChatHub. The launcher is the hub's sole client; browsers use REST +
///                 Bearer and never open the WebSocket.
/// ====================================================================================
/// </summary>
[ApiController]
[Route("auth")]
public class AuthSessionController(
    IW3CAuthenticationService authService,
    ITicketStore ticketStore,
    MintRateLimiter rateLimiter,
    TimeProvider time) : ControllerBase
{
    private readonly IW3CAuthenticationService _authService = authService;
    private readonly ITicketStore _ticketStore = ticketStore;
    private readonly MintRateLimiter _rateLimiter = rateLimiter;
    private readonly TimeProvider _time = time;

    [HttpPost("session")]
    public IActionResult MintTicket()
    {
        var now = _time.GetUtcNow().UtcDateTime;

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

        // Validate the signature only. Expiry is deliberately NOT enforced: this is not an admin path, and
        // players keep using old tokens - same as the other non-admin filters.
        // GetUserByToken THROWS on bad/garbage tokens (it never returns null) — treat any failure as 401.
        // No ticket is minted for an unvalidated caller.
        W3CUserAuthenticationDto identity;
        try
        {
            identity = _authService.GetUserByToken(token, validateLifetime: false);
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
            MaxRedemptions = SessionLimits.TicketMaxRedemptions,
        });
    }
}

public class TicketResponse
{
    public string Ticket { get; set; }
    public int ExpiresInSeconds { get; set; }

    /// <summary>Presentations the ticket accepts inside its TTL. Its presence tells the launcher it may
    /// reuse the ticket across reconnects; older servers omit it and burn the ticket on first use.</summary>
    public int MaxRedemptions { get; set; }
}
