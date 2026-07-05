using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Domain.ChatService;

namespace WC3ChampionsStatisticService.Tests.Friend;

[TestFixture]
public class RelationshipChangeNotifierTests
{
    private const string ChatApiUrl = "https://chat-service.test.local";
    private const string Secret = "test-secret-value";

    private static ChatPingSettings EnabledSettings() => new(ChatApiUrl, Secret);

    /// <summary>Same Moq.Protected HttpMessageHandler factory idiom as ChatServiceClientTests's
    /// CreateRoutingFactory: every SendAsync call is routed through <paramref name="responder"/>.</summary>
    private static (Mock<IHttpClientFactory> Factory, List<HttpRequestMessage> Requests) CreateFactory(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => requests.Add(request))
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) => responder(request));

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler.Object));

        return (factory, requests);
    }

    /// <summary>First call throws (simulates a transient network failure), second call succeeds.</summary>
    private static (Mock<IHttpClientFactory> Factory, List<HttpRequestMessage> Requests) CreateThrowingThenSucceedingFactory()
    {
        var requests = new List<HttpRequestMessage>();
        var callCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                requests.Add(request);
                callCount++;
            })
            .Returns<HttpRequestMessage, CancellationToken>((_, _) =>
            {
                if (callCount == 1) throw new HttpRequestException("simulated transient failure");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler.Object));

        return (factory, requests);
    }

    /// <summary>Every call throws, unconditionally.</summary>
    private static (Mock<IHttpClientFactory> Factory, List<HttpRequestMessage> Requests) CreateAlwaysThrowingFactory()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => requests.Add(request))
            .Returns<HttpRequestMessage, CancellationToken>((_, _) => throw new HttpRequestException("always fails"));

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler.Object));

        return (factory, requests);
    }

    [Test]
    public async Task SendWithRetryAsync_Success_SendsExactlyOneSignedRequest()
    {
        var (factory, requests) = CreateFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var notifier = new RelationshipChangeNotifier(factory.Object, EnabledSettings());

        await notifier.SendWithRetryAsync(RelationshipChangeType.Block, "Foo#1234", "Bar#5678");

        Assert.That(requests, Has.Count.EqualTo(1));
        var request = requests[0];
        Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(request.RequestUri!.ToString(), Is.EqualTo($"{ChatApiUrl}/internal/relationship-changes"));
        Assert.That(request.Content!.Headers.ContentType!.ToString(), Is.EqualTo("application/json; charset=utf-8"));

        var body = await request.Content.ReadAsStringAsync();
        Assert.That(body, Is.EqualTo("""{"type":"block","actor":"Foo#1234","target":"Bar#5678"}"""));

        var timestampHeader = request.Headers.GetValues(ChatInternalApiSigner.TimestampHeaderName).Single();
        var timestampSeconds = long.Parse(timestampHeader, CultureInfo.InvariantCulture);
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.That(Math.Abs(nowSeconds - timestampSeconds), Is.LessThanOrEqualTo(120));

        var signatureHeader = request.Headers.GetValues(ChatInternalApiSigner.SignatureHeaderName).Single();
        var expectedSignature = ChatInternalApiSigner.CreateSignatureHeaderValue(Secret, timestampHeader, body);
        Assert.That(signatureHeader, Is.EqualTo(expectedSignature));
    }

    [TestCase(RelationshipChangeType.Block, "block")]
    [TestCase(RelationshipChangeType.Unblock, "unblock")]
    [TestCase(RelationshipChangeType.FriendAdd, "friendAdd")]
    [TestCase(RelationshipChangeType.FriendRemove, "friendRemove")]
    public async Task SendWithRetryAsync_MapsAllFourWireLiterals(RelationshipChangeType type, string expectedLiteral)
    {
        var (factory, requests) = CreateFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var notifier = new RelationshipChangeNotifier(factory.Object, EnabledSettings());

        await notifier.SendWithRetryAsync(type, "Foo#1234", "Bar#5678");

        var body = await requests.Single().Content!.ReadAsStringAsync();
        var parsed = JObject.Parse(body);
        Assert.That(parsed["type"]!.Value<string>(), Is.EqualTo(expectedLiteral));
    }

    [Test]
    public void SendWithRetryAsync_NonSuccess_RetriesExactlyOnce_ThenGivesUp()
    {
        var (factory, requests) = CreateFactory(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var notifier = new RelationshipChangeNotifier(factory.Object, EnabledSettings());

        Assert.DoesNotThrowAsync(async () =>
            await notifier.SendWithRetryAsync(RelationshipChangeType.Block, "Foo#1234", "Bar#5678"));

        Assert.That(requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SendWithRetryAsync_FirstAttemptThrows_SecondSucceeds()
    {
        var (factory, requests) = CreateThrowingThenSucceedingFactory();
        var notifier = new RelationshipChangeNotifier(factory.Object, EnabledSettings());

        await notifier.SendWithRetryAsync(RelationshipChangeType.Block, "Foo#1234", "Bar#5678");

        Assert.That(requests, Has.Count.EqualTo(2));

        // Fresh timestamp+signature per attempt: each captured request must self-verify
        // independently against its own timestamp/body (not just the last one).
        foreach (var request in requests)
        {
            var timestampHeader = request.Headers.GetValues(ChatInternalApiSigner.TimestampHeaderName).Single();
            var signatureHeader = request.Headers.GetValues(ChatInternalApiSigner.SignatureHeaderName).Single();
            var body = await request.Content!.ReadAsStringAsync();
            var expectedSignature = ChatInternalApiSigner.CreateSignatureHeaderValue(Secret, timestampHeader, body);
            Assert.That(signatureHeader, Is.EqualTo(expectedSignature));
        }
    }

    [Test]
    public void SendWithRetryAsync_HandlerThrows_NeverPropagates()
    {
        var (factory, requests) = CreateAlwaysThrowingFactory();
        var notifier = new RelationshipChangeNotifier(factory.Object, EnabledSettings());

        Assert.DoesNotThrowAsync(async () =>
            await notifier.SendWithRetryAsync(RelationshipChangeType.Block, "Foo#1234", "Bar#5678"));

        Assert.That(requests, Has.Count.EqualTo(2));
    }

    /// <summary>Regression test for a review finding: body construction (including
    /// <c>ToWireLiteral</c>, which throws for any <see cref="RelationshipChangeType"/> outside its
    /// four known cases) must happen INSIDE the per-attempt try/catch, not before the loop, so a
    /// serialization/mapping failure is retried and logged like any other failure instead of
    /// silently faulting the returned <see cref="Task"/> with zero HTTP attempts and zero logging.
    /// Since the invalid enum value never changes between attempts, body construction throws on
    /// EVERY attempt: attempt 1 is swallowed by the `attempt &lt; MaxAttempts` retry filter, attempt
    /// 2 falls to the unconditional catch (logs once, returns) -- so no HTTP request is ever sent.</summary>
    [Test]
    public void SendWithRetryAsync_BodyConstructionThrowsForOutOfRangeEnum_NeverPropagates_AndSendsNoRequests()
    {
        var (factory, requests) = CreateFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var notifier = new RelationshipChangeNotifier(factory.Object, EnabledSettings());
        var outOfRangeType = (RelationshipChangeType)999;

        Assert.DoesNotThrowAsync(async () =>
            await notifier.SendWithRetryAsync(outOfRangeType, "Foo#1234", "Bar#5678"));

        Assert.That(requests, Is.Empty);
    }

    [Test]
    public void NotifyChange_Disabled_SendsNothing_AndDoesNotThrow()
    {
        var (factory, requests) = CreateFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var disabledSettings = new ChatPingSettings(ChatApiUrl, null); // no secret => disabled
        var notifier = new RelationshipChangeNotifier(factory.Object, disabledSettings);

        Assert.DoesNotThrow(() => notifier.NotifyChange(RelationshipChangeType.Block, "Foo#1234", "Bar#5678"));

        Assert.That(requests, Is.Empty);
        Assert.That(notifier.LastDispatch.IsCompleted, Is.True);
    }

    [Test]
    public async Task NotifyChange_Enabled_DispatchesInBackground()
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        var requests = new List<HttpRequestMessage>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => requests.Add(request))
            .Returns(() => tcs.Task);

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler.Object));

        var notifier = new RelationshipChangeNotifier(factory.Object, EnabledSettings());

        notifier.NotifyChange(RelationshipChangeType.FriendAdd, "Foo#1234", "Bar#5678");

        // NotifyChange must have RETURNED already without awaiting the HTTP call on the caller's
        // path -- the gated response task is still pending here (deterministic, no timing asserts).
        Assert.That(tcs.Task.IsCompleted, Is.False);

        tcs.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
        await notifier.LastDispatch;

        Assert.That(requests, Has.Count.EqualTo(1));
    }

    [TestCase("", "Bar#5678")]
    [TestCase("   ", "Bar#5678")]
    [TestCase(null, "Bar#5678")]
    [TestCase("Foo#1234", "")]
    [TestCase("Foo#1234", "   ")]
    [TestCase("Foo#1234", null)]
    public void NotifyChange_BlankActorOrTarget_SkipsSend(string actor, string target)
    {
        var (factory, requests) = CreateFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var notifier = new RelationshipChangeNotifier(factory.Object, EnabledSettings());

        Assert.DoesNotThrow(() => notifier.NotifyChange(RelationshipChangeType.Block, actor, target));

        Assert.That(requests, Is.Empty);
    }
}
