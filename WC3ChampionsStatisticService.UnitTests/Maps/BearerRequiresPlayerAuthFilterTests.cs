using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.Extensions;
using W3ChampionsStatisticService.Services.Interceptors;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;
using static WC3ChampionsStatisticService.Tests.AuthFilterTestHelper;

namespace WC3ChampionsStatisticService.Tests.Maps;

[TestFixture]
public class BearerRequiresPlayerAuthFilterTests
{
    private const string DenyError = "Invalid token";
    private const string UnexpectedFailureLogPrefix =
        "Player JWT could not be verified for a reason other than a rejected token: ";

    private Mock<ILogger<BearerRequiresPlayerAuthFilter>> _logger;

    [SetUp]
    public void SetUp()
    {
        _logger = new Mock<ILogger<BearerRequiresPlayerAuthFilter>>();
    }

    [Test]
    public async Task ValidToken_StoresBattleTagInItems_AndDoesNotShortCircuit()
    {
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken("good-token", false))
            .Returns(new W3CUserAuthenticationDto
            {
                BattleTag = "peter#123",
                Name = "peter",
                IsAdmin = false,
                Permissions = new HashSet<EPermission>(),
            });

        var context = CreateContext("Bearer good-token", out var body);

        await CreateFilter(auth.Object).OnAuthorizationAsync(context);

        Assert.That(context.Result, Is.Null, "a valid player token must not short-circuit the pipeline");
        Assert.That(context.HttpContext.Items[BearerRequiresPlayerAuthFilter.BattleTagItemKey],
            Is.EqualTo("peter#123"));
        Assert.That(body.WasRead, Is.False, "the request body must never be touched by authorization");
        AssertNothingLogged();
    }

    [Test]
    public async Task MissingAuthorizationHeader_Returns401_WithoutReadingBody()
    {
        var auth = new Mock<IW3CAuthenticationService>(MockBehavior.Strict);
        var context = CreateContext(null, out var body);

        await CreateFilter(auth.Object).OnAuthorizationAsync(context);

        AssertUnauthorizedWithError(context.Result, DenyError);
        Assert.That(body.WasRead, Is.False);
        auth.Verify(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task NonBearerScheme_Returns401()
    {
        var auth = new Mock<IW3CAuthenticationService>(MockBehavior.Strict);
        var context = CreateContext("Basic aGk6dGhlcmU=", out _);

        await CreateFilter(auth.Object).OnAuthorizationAsync(context);

        AssertUnauthorizedWithError(context.Result, DenyError);
        // The 401 alone is not proof: the filter's catch-all would also turn a strict-mock throw into a
        // 401, so pin that a non-Bearer credential is rejected before it ever reaches token validation.
        auth.Verify(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [TestCase("Bearer")]
    [TestCase("Bearer   ")]
    public async Task BearerSchemeWithoutCredential_Returns401_WithoutCallingTheService(string authorizationHeader)
    {
        var auth = new Mock<IW3CAuthenticationService>(MockBehavior.Strict);
        var context = CreateContext(authorizationHeader, out _);

        await CreateFilter(auth.Object).OnAuthorizationAsync(context);

        AssertUnauthorizedWithError(context.Result, DenyError);
        auth.Verify(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        AssertNothingLogged();
    }

    [Test]
    public async Task RejectedToken_Returns401_Quietly_AndDoesNotLeakTheException()
    {
        const string exceptionMessage = "IDX10511: Signature validation failed.";
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(It.IsAny<string>(), false))
            .Throws(new SecurityTokenInvalidSignatureException(exceptionMessage));

        var context = CreateContext("Bearer forged", out _);

        await CreateFilter(auth.Object).OnAuthorizationAsync(context);

        AssertUnauthorizedWithError(context.Result, DenyError);
        Assert.That(((ErrorResult)((UnauthorizedObjectResult)context.Result).Value).Error, Does.Not.Contain(exceptionMessage));
        Assert.That(context.HttpContext.Items.ContainsKey(BearerRequiresPlayerAuthFilter.BattleTagItemKey), Is.False);
        AssertNothingLogged();
    }

    // Tokens the REAL service rejects, covering each exception family IdentityModel 8.10 raises for a bad
    // client token, including the non-security types its JWE branch throws. This doubles as an upgrade canary:
    // if a future IdentityModel reports a malformed token from outside its own assemblies, the filter would
    // start logging it and this test fails.
    [TestCaseSource(nameof(TokensTheRealServiceRejects))]
    public async Task TokenRejectedByTheRealService_Returns401_Quietly(string token)
    {
        var context = CreateContext("Bearer " + token, out _);

        await CreateFilter(new W3CAuthenticationService()).OnAuthorizationAsync(context);

        AssertUnauthorizedWithError(context.Result, DenyError);
        Assert.That(context.HttpContext.Items.ContainsKey(BearerRequiresPlayerAuthFilter.BattleTagItemKey), Is.False);
        AssertNothingLogged();
    }

    [Test]
    public async Task UnreadableConfiguredPublicKey_Returns401_AndLogsOnlyTheExceptionType()
    {
        // A broken JWT_PUBLIC_KEY surfaces as an ArgumentException from the key import — the same exception
        // TYPE IdentityModel uses for an undecodable token — and must still be told apart and logged.
        using var rsa = RSA.Create(2048);
        var token = SignedToken(rsa, PlayerClaims());
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(token, false))
            .Returns((string jwt, bool validateLifetime) =>
                W3CUserAuthenticationDto.FromJWT(jwt, "-----BEGIN PUBLIC KEY-----\nnot-a-key\n-----END PUBLIC KEY-----\n", validateLifetime));
        var context = CreateContext("Bearer " + token, out _);

        await CreateFilter(auth.Object).OnAuthorizationAsync(context);

        AssertUnauthorizedWithError(context.Result, DenyError);
        Assert.That(context.HttpContext.Items.ContainsKey(BearerRequiresPlayerAuthFilter.BattleTagItemKey), Is.False);
        AssertLoggedOnlyTheExceptionType(typeof(ArgumentException));
    }

    [Test]
    public async Task CorrectlySignedTokenWithAnUnexpectedClaimShape_Returns401_AndLogsOnlyTheExceptionType()
    {
        // Models identification-service changing the claim shape: the signature verifies, FromJWT then fails
        // reading a missing `battleTag` claim (InvalidOperationException) — a server-side fault, not a bad token.
        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
        var token = SignedToken(rsa, [new Claim("isAdmin", "False"), new Claim("name", "peter")]);
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(token, false))
            .Returns((string jwt, bool validateLifetime) => W3CUserAuthenticationDto.FromJWT(jwt, publicKeyPem, validateLifetime));
        var context = CreateContext("Bearer " + token, out _);

        await CreateFilter(auth.Object).OnAuthorizationAsync(context);

        AssertUnauthorizedWithError(context.Result, DenyError);
        Assert.That(context.HttpContext.Items.ContainsKey(BearerRequiresPlayerAuthFilter.BattleTagItemKey), Is.False);
        AssertLoggedOnlyTheExceptionType(typeof(InvalidOperationException));
    }

    [Test]
    public async Task CorrectlySignedTokenWithANonBooleanIsAdminClaim_Returns401_AndLogsOnlyTheExceptionType()
    {
        // FormatException is also what IdentityModel's JWE branch throws for a malformed client token (quiet). Here
        // it comes from our own claim parsing (bool.Parse) after the signature verified, so it is a server-side fault
        // and must still be logged: the classification goes by where the exception was thrown, not by its type.
        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
        var token = SignedToken(rsa, [new Claim("battleTag", "peter#123"), new Claim("isAdmin", "maybe"), new Claim("name", "peter")]);
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(token, false))
            .Returns((string jwt, bool validateLifetime) => W3CUserAuthenticationDto.FromJWT(jwt, publicKeyPem, validateLifetime));
        var context = CreateContext("Bearer " + token, out _);

        await CreateFilter(auth.Object).OnAuthorizationAsync(context);

        AssertUnauthorizedWithError(context.Result, DenyError);
        Assert.That(context.HttpContext.Items.ContainsKey(BearerRequiresPlayerAuthFilter.BattleTagItemKey), Is.False);
        AssertLoggedOnlyTheExceptionType(typeof(FormatException));
    }

    [Test]
    public async Task FaultThrownFromAnotherMicrosoftAssembly_Returns401_AndLogsOnlyTheExceptionType()
    {
        // Only IdentityModel's own assemblies speak for the client's token. A fault thrown from any other Microsoft
        // library (here ASP.NET Core's PathString rejecting its input) is a server-side fault and must still be logged.
        static W3CUserAuthenticationDto ThrowFromAspNetCore() => new() { Name = new PathString("no-leading-slash").Value };
        var thrown = Assert.Throws<ArgumentException>(() => ThrowFromAspNetCore());
        Assert.That(thrown.TargetSite?.DeclaringType?.Assembly.GetName().Name,
            Does.StartWith("Microsoft.").And.Not.StartWith("Microsoft.IdentityModel."),
            "precondition: the fault must be thrown from a non-IdentityModel Microsoft assembly");
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken("token", false)).Returns(ThrowFromAspNetCore);
        var context = CreateContext("Bearer token", out _);

        await CreateFilter(auth.Object).OnAuthorizationAsync(context);

        AssertUnauthorizedWithError(context.Result, DenyError);
        AssertLoggedOnlyTheExceptionType(typeof(ArgumentException));
    }

    [Test]
    public async Task TokenWithoutBattleTag_Returns401()
    {
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(It.IsAny<string>(), false))
            .Returns(new W3CUserAuthenticationDto { BattleTag = "", Name = "", Permissions = new HashSet<EPermission>() });

        var context = CreateContext("Bearer no-btag", out _);

        await CreateFilter(auth.Object).OnAuthorizationAsync(context);

        AssertUnauthorizedWithError(context.Result, DenyError);
        Assert.That(context.HttpContext.Items.ContainsKey(BearerRequiresPlayerAuthFilter.BattleTagItemKey), Is.False,
            "the battleTag must only be published once the identity has been fully accepted");
    }

    [Test]
    public async Task AuthServiceReturningNull_Returns401()
    {
        // A loose mock returns null for an unconfigured call; the real service throws instead, but the
        // filter must stay fail-closed rather than dereference a null identity (a 500).
        var auth = new Mock<IW3CAuthenticationService>();
        var context = CreateContext("Bearer whatever", out _);

        await CreateFilter(auth.Object).OnAuthorizationAsync(context);

        AssertUnauthorizedWithError(context.Result, DenyError);
        Assert.That(context.HttpContext.Items.ContainsKey(BearerRequiresPlayerAuthFilter.BattleTagItemKey), Is.False);
    }

    [Test]
    public void Filter_IsAnAuthorizationFilter_SoItRunsBeforeModelBindingAndResourceFilters()
    {
        Assert.That(typeof(IAsyncAuthorizationFilter).IsAssignableFrom(typeof(BearerRequiresPlayerAuthFilter)), Is.True,
            "an action filter would run after model binding, i.e. after the body may already have been read");
    }

    [Test]
    public async Task Attribute_ResolvesTheFilterThroughTheProductionRegistration()
    {
        // Same registration as Program.cs (a Castle class proxy built by AddInterceptedTransient), with the
        // interceptor's own dependencies supplied instead of booting the host.
        using var activitySource = new ActivitySource(nameof(BearerRequiresPlayerAuthFilterTests));
        var services = new ServiceCollection();
        services.AddSingleton(activitySource);
        services.AddSingleton<TracingInterceptor>();
        services.AddLogging();
        services.AddSingleton(new Mock<IW3CAuthenticationService>(MockBehavior.Strict).Object);
        services.AddInterceptedTransient<BearerRequiresPlayerAuthFilter>();
        using var provider = services.BuildServiceProvider();

        var attribute = new BearerRequiresPlayerAuthAttribute();
        var filter = attribute.CreateInstance(provider);

        Assert.That(attribute.IsReusable, Is.False);
        Assert.That(filter, Is.InstanceOf<BearerRequiresPlayerAuthFilter>());
        var context = CreateContext(null, out _);
        await ((IAsyncAuthorizationFilter)filter).OnAuthorizationAsync(context);
        AssertUnauthorizedWithError(context.Result, DenyError);
    }

    private BearerRequiresPlayerAuthFilter CreateFilter(IW3CAuthenticationService authService) =>
        new(authService, _logger.Object);

    private void AssertNothingLogged() =>
        _logger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Never);

    /// <summary>
    /// Exactly one Warning whose rendered text is the fixed prefix plus the exception's type name, with no
    /// exception attached — so neither the token nor the exception message (which can quote it) is logged.
    /// </summary>
    private void AssertLoggedOnlyTheExceptionType(Type exceptionType)
    {
        var expectedMessage = UnexpectedFailureLogPrefix + exceptionType.FullName;
        _logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString() == expectedMessage),
                null,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
        _logger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    private static IEnumerable<TestCaseData> TokensTheRealServiceRejects()
    {
        yield return new TestCaseData("garbage").SetName("{m}(not a JWT: SecurityTokenMalformedException)");
        yield return new TestCaseData("a.b.c").SetName("{m}(undecodable segments: ArgumentException from the JWT decoder)");
        yield return new TestCaseData(
                Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}") + "." +
                Base64UrlEncoder.Encode("{\"battleTag\":\"peter#123\",\"isAdmin\":\"False\",\"name\":\"peter\"}") + ".")
            .SetName("{m}(unsigned alg none: SecurityTokenInvalidSignatureException)");

        using var foreignKey = RSA.Create(2048);
        yield return new TestCaseData(SignedToken(foreignKey, PlayerClaims()))
            .SetName("{m}(signed by a foreign key: SecurityTokenSignatureKeyNotFoundException)");

        // Five segments take IdentityModel's JWE branch, which fails with non-security exception types before any key
        // is consulted. Both are client-controlled and must stay quiet.
        yield return new TestCaseData(Base64UrlEncoder.Encode("{\"enc\":\"A256GCM\"}") + ".AAAA.AAAA.AAAA.AAAA")
            .SetName("{m}(JWE header without alg: NullReferenceException from the JWT handler)");
        yield return new TestCaseData(Base64UrlEncoder.Encode("{\"alg\":\"dir\",\"enc\":\"A256GCM\"}") + ".AAAA.A.AAAA.AAAA")
            .SetName("{m}(JWE with a 1-character IV: FormatException from Base64UrlEncoder)");
    }

    private static Claim[] PlayerClaims() =>
        [new Claim("battleTag", "peter#123"), new Claim("isAdmin", "False"), new Claim("name", "peter")];

    private static string SignedToken(RSA key, IEnumerable<Claim> claims)
    {
        var credentials = new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };
        return new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddDays(7), signingCredentials: credentials));
    }

    private static AuthorizationFilterContext CreateContext(string authorizationHeader, out ExplodingStream body)
    {
        var httpContext = CreateHttpContext(authorizationHeader);
        body = new ExplodingStream();
        httpContext.Request.Body = body;
        httpContext.Request.ContentType = "multipart/form-data; boundary=xyz";

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }

    /// <summary>A request body that fails loudly the moment anything touches it.</summary>
    private sealed class ExplodingStream : Stream
    {
        public bool WasRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            WasRead = true;
            throw new InvalidOperationException("the request body must not be read during authorization");
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
