using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Maps;

[TestFixture]
public class BearerRequiresPlayerAuthFilterTests
{
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

        await new BearerRequiresPlayerAuthFilter(auth.Object).OnAuthorizationAsync(context);

        Assert.That(context.Result, Is.Null, "a valid player token must not short-circuit the pipeline");
        Assert.That(context.HttpContext.Items[BearerRequiresPlayerAuthFilter.BattleTagItemKey],
            Is.EqualTo("peter#123"));
        Assert.That(body.WasRead, Is.False, "the request body must never be touched by authorization");
    }

    [Test]
    public async Task MissingAuthorizationHeader_Returns401_WithoutReadingBody()
    {
        var auth = new Mock<IW3CAuthenticationService>(MockBehavior.Strict);
        var context = CreateContext(null, out var body);

        await new BearerRequiresPlayerAuthFilter(auth.Object).OnAuthorizationAsync(context);

        Assert.That(context.Result, Is.InstanceOf<UnauthorizedObjectResult>());
        Assert.That(body.WasRead, Is.False);
        auth.Verify(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task NonBearerScheme_Returns401()
    {
        var auth = new Mock<IW3CAuthenticationService>(MockBehavior.Strict);
        var context = CreateContext("Basic aGk6dGhlcmU=", out _);

        await new BearerRequiresPlayerAuthFilter(auth.Object).OnAuthorizationAsync(context);

        Assert.That(context.Result, Is.InstanceOf<UnauthorizedObjectResult>());
        // The 401 alone is not proof: the filter's catch-all would also turn a strict-mock throw into a
        // 401, so pin that a non-Bearer credential is rejected before it ever reaches token validation.
        auth.Verify(a => a.GetUserByToken(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task ThrowingAuthService_Returns401_AndDoesNotLeakTheException()
    {
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(It.IsAny<string>(), false))
            .Throws(new SecurityTokenExpiredException("expired"));

        var context = CreateContext("Bearer stale", out _);

        await new BearerRequiresPlayerAuthFilter(auth.Object).OnAuthorizationAsync(context);

        Assert.That(context.Result, Is.InstanceOf<UnauthorizedObjectResult>());
        Assert.That(context.HttpContext.Items.ContainsKey(BearerRequiresPlayerAuthFilter.BattleTagItemKey), Is.False);
    }

    [Test]
    public async Task TokenWithoutBattleTag_Returns401()
    {
        var auth = new Mock<IW3CAuthenticationService>();
        auth.Setup(a => a.GetUserByToken(It.IsAny<string>(), false))
            .Returns(new W3CUserAuthenticationDto { BattleTag = "", Name = "", Permissions = new HashSet<EPermission>() });

        var context = CreateContext("Bearer no-btag", out _);

        await new BearerRequiresPlayerAuthFilter(auth.Object).OnAuthorizationAsync(context);

        Assert.That(context.Result, Is.InstanceOf<UnauthorizedObjectResult>());
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

        await new BearerRequiresPlayerAuthFilter(auth.Object).OnAuthorizationAsync(context);

        Assert.That(context.Result, Is.InstanceOf<UnauthorizedObjectResult>());
        Assert.That(context.HttpContext.Items.ContainsKey(BearerRequiresPlayerAuthFilter.BattleTagItemKey), Is.False);
    }

    [Test]
    public void Filter_IsAnAuthorizationFilter_SoItRunsBeforeModelBindingAndResourceFilters()
    {
        Assert.That(typeof(IAsyncAuthorizationFilter).IsAssignableFrom(typeof(BearerRequiresPlayerAuthFilter)), Is.True,
            "an action filter would run after model binding, i.e. after the body may already have been read");
    }

    [Test]
    public void Attribute_IsFilterFactory_ResolvingFilterFromDI()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IW3CAuthenticationService>());
        services.AddTransient<BearerRequiresPlayerAuthFilter>();
        var provider = services.BuildServiceProvider();

        var attribute = new BearerRequiresPlayerAuthAttribute();

        Assert.That(attribute.IsReusable, Is.False);
        Assert.That(attribute.CreateInstance(provider), Is.TypeOf<BearerRequiresPlayerAuthFilter>());
    }

    private static AuthorizationFilterContext CreateContext(string authorizationHeader, out ExplodingStream body)
    {
        var httpContext = new DefaultHttpContext();
        body = new ExplodingStream();
        httpContext.Request.Body = body;
        httpContext.Request.ContentType = "multipart/form-data; boundary=xyz";
        if (authorizationHeader != null)
        {
            httpContext.Request.Headers[HeaderNames.Authorization] = authorizationHeader;
        }

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
