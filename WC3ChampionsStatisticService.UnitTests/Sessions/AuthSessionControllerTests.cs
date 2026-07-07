using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Sessions;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Sessions;

/// <summary>
/// REST face of the ticket-mint contract (WB-1). Constructs <see cref="AuthSessionController"/>
/// directly with a <see cref="DefaultHttpContext"/> (no TestHost). wb's real
/// <see cref="W3CAuthenticationService"/> reads its RSA key from a static env-var field and has no
/// test-key constructor (unlike chat-service's), so <see cref="IW3CAuthenticationService"/> is mocked;
/// <see cref="TicketStore"/> and <see cref="MintRateLimiter"/> are real (concrete, non-virtual — the
/// same choice chat-service's controller tests make) so single-use and 429 behavior is asserted for real.
/// </summary>
[TestFixture]
public class AuthSessionControllerTests
{
    private static DefaultHttpContext BuildHttpContext(string authorizationHeader)
    {
        var context = new DefaultHttpContext();
        if (authorizationHeader != null)
        {
            context.Request.Headers[HeaderNames.Authorization] = authorizationHeader;
        }
        return context;
    }

    private static AuthSessionController BuildController(
        IW3CAuthenticationService authService, ITicketStore ticketStore, MintRateLimiter limiter,
        string authorizationHeader) =>
        new(authService, ticketStore, limiter)
        {
            ControllerContext = new ControllerContext { HttpContext = BuildHttpContext(authorizationHeader) }
        };

    private static Mock<IW3CAuthenticationService> AuthReturning(string battleTag)
    {
        var mock = new Mock<IW3CAuthenticationService>();
        mock.Setup(a => a.GetUserByToken(It.IsAny<string>(), true))
            .Returns(new W3CUserAuthenticationDto { BattleTag = battleTag, Name = battleTag.Split('#')[0] });
        return mock;
    }

    [Test]
    public void ValidJwt_Returns200_WithSingleUseTicketAnd60Seconds()
    {
        var authService = AuthReturning("peter#123");
        var ticketStore = new TicketStore();
        var limiter = new MintRateLimiter();
        var controller = BuildController(authService.Object, ticketStore, limiter, "Bearer good-jwt");

        var result = controller.MintTicket();

        var okResult = result as OkObjectResult;
        Assert.IsNotNull(okResult, "A valid, non-expired JWT must mint a ticket (200).");
        Assert.AreEqual(200, okResult.StatusCode);
        var response = okResult.Value as TicketResponse;
        Assert.IsNotNull(response, "Response body must be a TicketResponse.");
        Assert.AreEqual(SessionLimits.TicketTtlSeconds, response.ExpiresInSeconds);
        Assert.AreEqual(60, response.ExpiresInSeconds);
        Assert.IsNotNull(response.Ticket);
        Assert.AreEqual(64, response.Ticket.Length);

        // The minted ticket must be single-use against the SAME store the controller minted from.
        Assert.IsTrue(ticketStore.TryConsume(response.Ticket, DateTime.UtcNow, out var identity));
        Assert.AreEqual("peter#123", identity.BattleTag);
        Assert.IsFalse(ticketStore.TryConsume(response.Ticket, DateTime.UtcNow, out _), "single-use");
    }

    [Test]
    public void InvalidJwt_Returns401()
    {
        var authService = new Mock<IW3CAuthenticationService>();
        authService.Setup(a => a.GetUserByToken(It.IsAny<string>(), true))
            .Throws(new SecurityTokenException("bad signature"));
        var ticketStore = new TicketStore();
        var controller = BuildController(authService.Object, ticketStore, new MintRateLimiter(), "Bearer garbage");

        var result = controller.MintTicket();

        Assert.IsInstanceOf<UnauthorizedResult>(result, "An invalid JWT must be rejected with 401.");
        Assert.AreEqual(0, ticketStore.Count, "No ticket must be minted for an invalid JWT.");
    }

    [Test]
    public void ExpiredJwt_Returns401()
    {
        var authService = new Mock<IW3CAuthenticationService>();
        authService.Setup(a => a.GetUserByToken(It.IsAny<string>(), true))
            .Throws(new SecurityTokenExpiredException("expired"));
        var ticketStore = new TicketStore();
        var controller = BuildController(authService.Object, ticketStore, new MintRateLimiter(), "Bearer expired-jwt");

        var result = controller.MintTicket();

        Assert.IsInstanceOf<UnauthorizedResult>(result, "An expired JWT must be rejected with 401.");
        Assert.AreEqual(0, ticketStore.Count);
    }

    [Test]
    public void MissingAuthorizationHeader_Returns401()
    {
        var authService = AuthReturning("unused#1");
        var ticketStore = new TicketStore();
        var controller = BuildController(authService.Object, ticketStore, new MintRateLimiter(), authorizationHeader: null);

        var result = controller.MintTicket();

        Assert.IsInstanceOf<UnauthorizedResult>(result, "A missing Authorization header must return 401.");
        Assert.AreEqual(0, ticketStore.Count);
        authService.Verify(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()), Times.Never,
            "A missing header must short-circuit before JWT validation.");
    }

    [Test]
    public void NonBearerScheme_Returns401()
    {
        var authService = AuthReturning("unused#1");
        var ticketStore = new TicketStore();
        var controller = BuildController(authService.Object, ticketStore, new MintRateLimiter(), "Basic xyz");

        var result = controller.MintTicket();

        Assert.IsInstanceOf<UnauthorizedResult>(result, "A non-Bearer scheme must return 401.");
        Assert.AreEqual(0, ticketStore.Count);
    }

    [Test]
    public void EleventhMint_SameBattleTag_Returns429()
    {
        // Per-battleTag mint limit is 10/min. The 11th within the window must be rate-limited.
        var authService = AuthReturning("peter#123");
        var ticketStore = new TicketStore();
        var limiter = new MintRateLimiter();

        for (var i = 0; i < SessionLimits.TicketMintPerBattleTagLimit; i++)
        {
            var result = BuildController(authService.Object, ticketStore, limiter, "Bearer good-jwt").MintTicket();
            Assert.IsInstanceOf<OkObjectResult>(result, $"mint {i + 1} of {SessionLimits.TicketMintPerBattleTagLimit} should succeed");
        }

        var eleventh = BuildController(authService.Object, ticketStore, limiter, "Bearer good-jwt").MintTicket();

        Assert.IsInstanceOf<StatusCodeResult>(eleventh, "The 11th mint within the window must be rate-limited.");
        Assert.AreEqual(StatusCodes.Status429TooManyRequests, ((StatusCodeResult)eleventh).StatusCode);
    }
}
