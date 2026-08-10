using System;
using System.Collections.Generic;
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
public class FlairChangeNotifierTests
{
    private List<HttpRequestMessage> _captured;
    private List<string> _capturedBodies;

    private FlairChangeNotifier CreateNotifier(ChatPingSettings settings, HttpStatusCode status = HttpStatusCode.OK)
    {
        _captured = new List<HttpRequestMessage>();
        _capturedBodies = new List<string>();

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken _) =>
            {
                _captured.Add(request);
                _capturedBodies.Add(request.Content == null ? null : await request.Content.ReadAsStringAsync());
                return new HttpResponseMessage(status);
            });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler.Object));

        return new FlairChangeNotifier(factory.Object, settings);
    }

    private static ChatPingSettings Enabled() => new("https://chat.test", "test-secret");

    /// <summary>Every SendAsync call throws, unconditionally -- used to exercise the
    /// <c>catch (Exception) when (attempt &lt; MaxAttempts)</c> / <c>catch (Exception e)</c> branches
    /// in <see cref="FlairChangeNotifier"/> with an ENABLED notifier, mirroring
    /// RelationshipChangeNotifierTests.CreateAlwaysThrowingFactory.</summary>
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
    public async Task NotifyChanged_PostsTheBattleTagsToProfileChanges()
    {
        var notifier = CreateNotifier(Enabled());

        notifier.NotifyChanged(new[] { "Foo#1234", "Bar#5678" });
        await notifier.LastDispatch;

        Assert.That(_captured, Has.Count.EqualTo(1));
        Assert.That(_captured[0].Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(_captured[0].RequestUri.ToString(), Is.EqualTo("https://chat.test/internal/profile-changes"));

        var body = JObject.Parse(_capturedBodies[0]);
        Assert.That(body["battleTags"].Select(t => t.ToString()), Is.EqualTo(new[] { "Foo#1234", "Bar#5678" }));
    }

    [Test]
    public async Task NotifyChanged_SignsWithTheSharedHmacScheme()
    {
        var notifier = CreateNotifier(Enabled());

        notifier.NotifyChanged(new[] { "Foo#1234" });
        await notifier.LastDispatch;

        var timestamp = _captured[0].Headers.GetValues(ChatInternalApiSigner.TimestampHeaderName).Single();
        var signature = _captured[0].Headers.GetValues(ChatInternalApiSigner.SignatureHeaderName).Single();

        // The signature must be over the EXACT body bytes that were sent — a mismatch here is the
        // classic "serialized twice" bug and chat-service would reject every ping.
        Assert.That(signature, Is.EqualTo(
            ChatInternalApiSigner.CreateSignatureHeaderValue("test-secret", timestamp, _capturedBodies[0])));
    }

    [Test]
    public async Task NotifyChanged_ChunksAtSixtyFour()
    {
        // The receiver rejects any batch over ChatLimits.InternalMaxMembersPerCall (64) outright, and a
        // clan delete can affect more members than that — so an unchunked notifier would silently lose
        // the whole notification for exactly the largest clans.
        var notifier = CreateNotifier(Enabled());
        var tags = Enumerable.Range(0, 150).Select(i => $"Player{i}#1").ToArray();

        notifier.NotifyChanged(tags);
        await notifier.LastDispatch;

        Assert.That(_captured, Has.Count.EqualTo(3));
        var sent = _capturedBodies.SelectMany(b => JObject.Parse(b)["battleTags"].Select(t => t.ToString())).ToList();
        Assert.That(sent, Is.EqualTo(tags));
        Assert.That(JObject.Parse(_capturedBodies[0])["battleTags"].Count(), Is.EqualTo(64));
        Assert.That(JObject.Parse(_capturedBodies[2])["battleTags"].Count(), Is.EqualTo(22));
    }

    [Test]
    public async Task NotifyChanged_ExactlySixtyFourTags_ProducesExactlyOneRequest()
    {
        // The receiver rejects any batch over 64 outright, so the boundary itself -- not just an
        // over-sized case -- is what matters: 64 must stay a single request, not get chunked early.
        var notifier = CreateNotifier(Enabled());
        var tags = Enumerable.Range(0, 64).Select(i => $"Player{i}#1").ToArray();

        notifier.NotifyChanged(tags);
        await notifier.LastDispatch;

        Assert.That(_captured, Has.Count.EqualTo(1));
        Assert.That(JObject.Parse(_capturedBodies[0])["battleTags"].Count(), Is.EqualTo(64));
    }

    [Test]
    public async Task NotifyChanged_SixtyFiveTags_SplitsIntoSixtyFourPlusOne()
    {
        var notifier = CreateNotifier(Enabled());
        var tags = Enumerable.Range(0, 65).Select(i => $"Player{i}#1").ToArray();

        notifier.NotifyChanged(tags);
        await notifier.LastDispatch;

        Assert.That(_captured, Has.Count.EqualTo(2));
        Assert.That(JObject.Parse(_capturedBodies[0])["battleTags"].Count(), Is.EqualTo(64));
        Assert.That(JObject.Parse(_capturedBodies[1])["battleTags"].Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task NotifyChanged_DedupesAndDropsBlanks()
    {
        var notifier = CreateNotifier(Enabled());

        notifier.NotifyChanged(new[] { "Foo#1234", "foo#1234", "  ", null, "Bar#5678" });
        await notifier.LastDispatch;

        var sent = JObject.Parse(_capturedBodies[0])["battleTags"].Select(t => t.ToString()).ToList();
        Assert.That(sent, Is.EqualTo(new[] { "Foo#1234", "Bar#5678" }));
    }

    [Test]
    public void NotifyChanged_WhenDisabled_SendsNothing()
    {
        var notifier = CreateNotifier(new ChatPingSettings("https://chat.test", null));

        notifier.NotifyChanged(new[] { "Foo#1234" });

        Assert.That(notifier.LastDispatch.IsCompleted, Is.True);
        Assert.That(_captured, Is.Empty);
    }

    [Test]
    public void NotifyChanged_WithNoUsableTags_SendsNothing()
    {
        var notifier = CreateNotifier(Enabled());

        notifier.NotifyChanged(new[] { "   ", null });

        Assert.That(notifier.LastDispatch.IsCompleted, Is.True);
        Assert.That(_captured, Is.Empty);
    }

    [Test]
    public async Task NotifyChanged_OnNonSuccess_RetriesOnceThenGivesUp()
    {
        var notifier = CreateNotifier(Enabled(), HttpStatusCode.InternalServerError);

        notifier.NotifyChanged(new[] { "Foo#1234" });
        await notifier.LastDispatch;

        Assert.That(_captured, Has.Count.EqualTo(2));
    }

    [Test]
    public void NotifyChanged_WhenDisabled_ConstructorAndNotifyNeverTouchTheHttpClientFactory()
    {
        // NOT coverage of the retry/exception branches in SendWithRetryAsync -- see
        // NotifyChanged_HandlerThrows_NeverPropagates for that. With a null secret,
        // ChatPingSettings.Enabled is false, so NotifyChanged returns at its very first line before
        // Task.Run/SendAllAsync/factory.CreateClient() are ever reached. This test only proves that
        // the disabled path never eagerly builds an HttpClient: factory.CreateClient() is rigged to
        // throw specifically so that reaching it would fail the test.
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Throws(new InvalidOperationException("boom"));

        Assert.DoesNotThrow(() =>
        {
            var notifier = new FlairChangeNotifier(factory.Object, new ChatPingSettings("https://chat.test", null));
            notifier.NotifyChanged(new[] { "Foo#1234" });
        });
    }

    [Test]
    public async Task NotifyChanged_HandlerThrows_NeverPropagates()
    {
        // Enabled settings (real url AND real secret) so NotifyChanged actually reaches Task.Run ->
        // SendAllAsync -> SendWithRetryAsync, and every SendAsync call throws -- this is what
        // genuinely exercises the `catch (Exception) when (attempt < MaxAttempts)` retry branch and
        // the final unconditional `catch (Exception e)` give-up branch, unlike the disabled-path test
        // above. Modeled on RelationshipChangeNotifierTests.SendWithRetryAsync_HandlerThrows_NeverPropagates.
        var (factory, requests) = CreateAlwaysThrowingFactory();
        var notifier = new FlairChangeNotifier(factory.Object, Enabled());

        notifier.NotifyChanged(new[] { "Foo#1234" });

        await notifier.LastDispatch;

        // MaxAttempts == 2: proves both the retry AND the give-up branch actually ran, not a
        // short-circuit on Enabled.
        Assert.That(requests, Has.Count.EqualTo(2));
    }
}
