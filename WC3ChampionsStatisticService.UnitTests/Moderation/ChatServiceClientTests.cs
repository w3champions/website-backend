using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using W3C.Contracts.Admin.Moderation;
using W3C.Domain.ChatService;

namespace WC3ChampionsStatisticService.Tests.Moderation;

[TestFixture]
public class ChatServiceClientTests
{
    private static (Mock<IHttpClientFactory> Factory, List<HttpRequestMessage> Requests) CreateFactory(
        HttpStatusCode statusCode,
        string content)
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => requests.Add(request))
            .ReturnsAsync(() => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler.Object));

        return (factory, requests);
    }

    /// <summary>
    /// Routes each request through <paramref name="responder"/> so tests can script distinct
    /// per-endpoint (and per-call) responses -- needed for the channels-list vs. messages-page
    /// vs. stale-cache-retry flows exercised by <c>GetChatRoomMessages</c>.
    /// </summary>
    private static (Mock<IHttpClientFactory> Factory, List<HttpRequestMessage> Requests) CreateRoutingFactory(
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

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    [Test]
    public async Task GetModerationChannels_RequestsChannelsUrl_WithBearerAndLimit500()
    {
        var (factory, requests) = CreateFactory(HttpStatusCode.OK, "[]");
        var client = new ChatServiceClient(factory.Object);

        await client.GetModerationChannels("token-abc");

        Assert.That(requests, Has.Count.EqualTo(1));
        Assert.That(requests[0].RequestUri!.PathAndQuery, Is.EqualTo("/api/moderation/channels?limit=500"));
        Assert.That(requests[0].Headers.Authorization?.ToString(), Is.EqualTo("Bearer token-abc"));
    }

    [Test]
    public async Task GetModerationChannels_DeserializesNumericEnumType()
    {
        const string json = """
        [
          {
            "id": "abc",
            "name": "W3C Lounge",
            "type": 0,
            "systemKind": null,
            "systemRef": null,
            "lastSeq": 48213,
            "lastMessageAt": "2026-07-03T21:14:02.331Z"
          }
        ]
        """;
        var (factory, _) = CreateFactory(HttpStatusCode.OK, json);
        var client = new ChatServiceClient(factory.Object);

        var channels = await client.GetModerationChannels("token-abc");

        Assert.That(channels, Has.Length.EqualTo(1));
        Assert.That(channels[0].Id, Is.EqualTo("abc"));
        Assert.That(channels[0].Name, Is.EqualTo("W3C Lounge"));
        Assert.That(channels[0].Type, Is.EqualTo(ChatChannelType.Public));
    }

    [Test]
    public async Task GetModerationChannels_DeserializesStringEnumType()
    {
        const string json = """
        [
          {
            "id": "abc",
            "name": "W3C Lounge",
            "type": "Public",
            "systemKind": null,
            "systemRef": null,
            "lastSeq": 48213,
            "lastMessageAt": "2026-07-03T21:14:02.331Z"
          }
        ]
        """;
        var (factory, _) = CreateFactory(HttpStatusCode.OK, json);
        var client = new ChatServiceClient(factory.Object);

        var channels = await client.GetModerationChannels("token-abc");

        Assert.That(channels, Has.Length.EqualTo(1));
        Assert.That(channels[0].Type, Is.EqualTo(ChatChannelType.Public));
    }

    [Test]
    public async Task GetModerationChannelMessages_RequestsMessagesUrl_NewestPage()
    {
        const string json = """{ "channelId": "abc123", "messages": [], "nextBeforeSeq": null }""";
        var (factory, requests) = CreateFactory(HttpStatusCode.OK, json);
        var client = new ChatServiceClient(factory.Object);

        await client.GetModerationChannelMessages("abc123", "token-abc");

        Assert.That(requests, Has.Count.EqualTo(1));
        Assert.That(requests[0].RequestUri!.PathAndQuery, Is.EqualTo("/api/moderation/channels/abc123/messages?limit=100"));
        Assert.That(requests[0].Headers.Authorization?.ToString(), Is.EqualTo("Bearer token-abc"));
    }

    [Test]
    public async Task GetModerationChannelMessages_DeserializesPage()
    {
        // Verbatim from the chat-service README's "REST API for the website-backend" example.
        const string json = """
        {
          "channelId": "665f1b2c9a1e4a0012abc123",
          "messages": [
            {
              "id": "665f1b3a9a1e4a0012def001",
              "channelId": "665f1b2c9a1e4a0012abc123",
              "seq": 48198,
              "senderBattleTag": "Peter#123",
              "senderName": "Peter",
              "content": "gl hf",
              "sentAt": "2026-07-03T21:10:00.000Z",
              "deleted": false,
              "deletedBy": null,
              "deletedAt": null,
              "shadow": false
            },
            {
              "id": "665f1b3a9a1e4a0012def002",
              "channelId": "665f1b2c9a1e4a0012abc123",
              "seq": 48199,
              "senderBattleTag": "Spammer#456",
              "senderName": "Spammer",
              "content": "buy gold at ...",
              "sentAt": "2026-07-03T21:10:05.000Z",
              "deleted": true,
              "deletedBy": "mod#1",
              "deletedAt": "2026-07-03T21:11:00.000Z",
              "shadow": false
            },
            {
              "id": "665f1b3a9a1e4a0012def003",
              "channelId": "665f1b2c9a1e4a0012abc123",
              "seq": 48200,
              "senderBattleTag": "Shadow#789",
              "senderName": "Shadow",
              "content": "spam spam spam",
              "sentAt": "2026-07-03T21:12:00.000Z",
              "deleted": false,
              "deletedBy": null,
              "deletedAt": null,
              "shadow": true
            }
          ],
          "nextBeforeSeq": 48198
        }
        """;
        var (factory, _) = CreateFactory(HttpStatusCode.OK, json);
        var client = new ChatServiceClient(factory.Object);

        var page = await client.GetModerationChannelMessages("665f1b2c9a1e4a0012abc123", "token-abc");

        Assert.That(page.Messages, Has.Length.EqualTo(3));
        Assert.That(page.NextBeforeSeq, Is.EqualTo(48198));

        var normal = page.Messages[0];
        Assert.That(normal.Id, Is.EqualTo("665f1b3a9a1e4a0012def001"));
        Assert.That(normal.Content, Is.EqualTo("gl hf"));
        Assert.That(normal.SenderBattleTag, Is.EqualTo("Peter#123"));
        Assert.That(normal.Deleted, Is.False);
        Assert.That(normal.Shadow, Is.False);
        Assert.That(normal.SentAt.Kind, Is.EqualTo(DateTimeKind.Utc));

        var deleted = page.Messages[1];
        Assert.That(deleted.Deleted, Is.True);
        Assert.That(deleted.Shadow, Is.False);

        var shadow = page.Messages[2];
        Assert.That(shadow.Deleted, Is.False);
        Assert.That(shadow.Shadow, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void GetModerationChannels_NonSuccess_ThrowsHttpRequestExceptionWithStatus(bool callChannelsEndpoint)
    {
        var (factory, _) = CreateFactory(HttpStatusCode.ServiceUnavailable, "service unavailable");
        var client = new ChatServiceClient(factory.Object);

        var exception = callChannelsEndpoint
            ? Assert.ThrowsAsync<HttpRequestException>(async () => await client.GetModerationChannels("token-abc"))
            : Assert.ThrowsAsync<HttpRequestException>(async () => await client.GetModerationChannelMessages("abc123", "token-abc"));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }

    [Test]
    public async Task GetChatRoomMessages_ResolvesPublicChannelByName_CaseInsensitive()
    {
        const string channelsJson = """
        [
          { "id": "ch1", "name": "W3C Lounge", "type": 0 },
          { "id": "sys1", "name": "Match 12345", "type": 2 }
        ]
        """;
        const string messagesJson = """
        {
          "messages": [
            {
              "id": "msg1",
              "content": "gl hf",
              "sentAt": "2026-07-03T21:10:00.000Z",
              "senderBattleTag": "Peter#123",
              "deleted": false,
              "shadow": false
            }
          ],
          "nextBeforeSeq": null
        }
        """;

        var (factory, requests) = CreateRoutingFactory(request =>
            request.RequestUri!.AbsolutePath.Contains("/messages")
                ? JsonResponse(HttpStatusCode.OK, messagesJson)
                : JsonResponse(HttpStatusCode.OK, channelsJson));

        var client = new ChatServiceClient(factory.Object);

        var messages = await client.GetChatRoomMessages("w3c lounge", "token-abc");

        Assert.That(requests, Has.Count.EqualTo(2));
        Assert.That(requests.Any(r => r.RequestUri!.AbsolutePath.StartsWith("/api/chat")), Is.False);
        Assert.That(requests[1].RequestUri!.PathAndQuery, Is.EqualTo("/api/moderation/channels/ch1/messages?limit=100"));

        Assert.That(messages, Has.Length.EqualTo(1));
        Assert.That(messages[0].Id, Is.EqualTo("msg1"));
        Assert.That(messages[0].Message, Is.EqualTo("gl hf"));
        Assert.That(messages[0].BattleTag, Is.EqualTo("Peter#123"));

        DateTime roundTrip = DateTime.Parse(messages[0].Time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.That(roundTrip, Is.EqualTo(new DateTime(2026, 7, 3, 21, 10, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task GetChatRoomMessages_FiltersDeletedAndShadowRows()
    {
        const string channelsJson = """[ { "id": "ch1", "name": "W3C Lounge", "type": 0 } ]""";
        // Verbatim from the chat-service README's "REST API for the website-backend" example.
        const string messagesJson = """
        {
          "messages": [
            {
              "id": "665f1b3a9a1e4a0012def001",
              "senderBattleTag": "Peter#123",
              "content": "gl hf",
              "sentAt": "2026-07-03T21:10:00.000Z",
              "deleted": false,
              "shadow": false
            },
            {
              "id": "665f1b3a9a1e4a0012def002",
              "senderBattleTag": "Spammer#456",
              "content": "buy gold at ...",
              "sentAt": "2026-07-03T21:10:05.000Z",
              "deleted": true,
              "shadow": false
            },
            {
              "id": "665f1b3a9a1e4a0012def003",
              "senderBattleTag": "Shadow#789",
              "content": "spam spam spam",
              "sentAt": "2026-07-03T21:12:00.000Z",
              "deleted": false,
              "shadow": true
            }
          ],
          "nextBeforeSeq": 48198
        }
        """;

        var (factory, _) = CreateRoutingFactory(request =>
            request.RequestUri!.AbsolutePath.Contains("/messages")
                ? JsonResponse(HttpStatusCode.OK, messagesJson)
                : JsonResponse(HttpStatusCode.OK, channelsJson));

        var client = new ChatServiceClient(factory.Object);

        var messages = await client.GetChatRoomMessages("W3C Lounge", "token-abc");

        Assert.That(messages, Has.Length.EqualTo(1));
        Assert.That(messages[0].Id, Is.EqualTo("665f1b3a9a1e4a0012def001"));
        Assert.That(messages[0].Message, Is.EqualTo("gl hf"));
        Assert.That(messages[0].BattleTag, Is.EqualTo("Peter#123"));
    }

    [Test]
    public async Task GetChatRoomMessages_UnknownRoom_ReturnsEmptyArray()
    {
        const string channelsJson = """[ { "id": "ch1", "name": "W3C Lounge", "type": 0 } ]""";

        var (factory, requests) = CreateRoutingFactory(_ => JsonResponse(HttpStatusCode.OK, channelsJson));

        var client = new ChatServiceClient(factory.Object);

        var messages = await client.GetChatRoomMessages("does-not-exist", "token-abc");

        Assert.That(messages, Is.Empty);
        Assert.That(requests, Has.Count.EqualTo(1));
        Assert.That(requests[0].RequestUri!.AbsolutePath, Is.EqualTo("/api/moderation/channels"));
    }

    [TestCase("../etc")]
    [TestCase("a/b")]
    [TestCase("../../api/moderation/channels/ch1/messages")]
    public async Task GetChatRoomMessages_PathTraversalLikeName_NeverReachesRawUrlPath(string maliciousRoomName)
    {
        const string channelsJson = """[ { "id": "ch1", "name": "W3C Lounge", "type": 0 } ]""";

        var (factory, requests) = CreateRoutingFactory(_ => JsonResponse(HttpStatusCode.OK, channelsJson));

        var client = new ChatServiceClient(factory.Object);

        var messages = await client.GetChatRoomMessages(maliciousRoomName, "token-abc");

        // Unmatched (no channel in the resolved list has this name) -> [] per Decision 4, and the
        // raw caller-supplied string is proven to never have been used as a URL path segment: only
        // one request (the channels-list lookup) was ever sent, and no request path contains it.
        Assert.That(messages, Is.Empty);
        Assert.That(requests, Has.Count.EqualTo(1));
        Assert.That(requests[0].RequestUri!.AbsolutePath, Is.EqualTo("/api/moderation/channels"));
        Assert.That(requests.All(r => !r.RequestUri!.AbsolutePath.Contains(maliciousRoomName)), Is.True);
    }

    [Test]
    public async Task GetChatRoomMessages_CachesResolution_SecondCallSkipsChannelList()
    {
        const string channelsJson = """[ { "id": "ch1", "name": "W3C Lounge", "type": 0 } ]""";
        const string messagesJson = """{ "messages": [], "nextBeforeSeq": null }""";

        var (factory, requests) = CreateRoutingFactory(request =>
            request.RequestUri!.AbsolutePath.Contains("/messages")
                ? JsonResponse(HttpStatusCode.OK, messagesJson)
                : JsonResponse(HttpStatusCode.OK, channelsJson));

        var client = new ChatServiceClient(factory.Object);

        await client.GetChatRoomMessages("W3C Lounge", "token-abc");
        await client.GetChatRoomMessages("w3c lounge", "token-abc"); // different case, same normalized cache key

        int channelsListRequests = requests.Count(r => r.RequestUri!.AbsolutePath == "/api/moderation/channels");
        Assert.That(channelsListRequests, Is.EqualTo(1));
    }

    [Test]
    public async Task GetChatRoomMessages_StaleCachedId_404_EvictsAndReresolvesOnce()
    {
        int channelsListCalls = 0;
        int ch1MessagesCalls = 0;

        var (factory, requests) = CreateRoutingFactory(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/moderation/channels")
            {
                channelsListCalls++;
                string channelId = channelsListCalls == 1 ? "ch1" : "ch2";
                return JsonResponse(HttpStatusCode.OK, $$"""[ { "id": "{{channelId}}", "name": "W3C Lounge", "type": 0 } ]""");
            }

            if (path == "/api/moderation/channels/ch1/messages")
            {
                ch1MessagesCalls++;
                // First call seeds the cache successfully; the second call simulates the id having
                // gone stale server-side (channel recreated with a new id) via a 404.
                return ch1MessagesCalls == 1
                    ? JsonResponse(HttpStatusCode.OK, """{ "messages": [], "nextBeforeSeq": null }""")
                    : JsonResponse(HttpStatusCode.NotFound, "not found");
            }

            if (path == "/api/moderation/channels/ch2/messages")
            {
                return JsonResponse(HttpStatusCode.OK, """{ "messages": [], "nextBeforeSeq": null }""");
            }

            throw new InvalidOperationException($"Unexpected request path: {path}");
        });

        var client = new ChatServiceClient(factory.Object);

        await client.GetChatRoomMessages("W3C Lounge", "token-abc"); // seeds cache -> ch1
        await client.GetChatRoomMessages("W3C Lounge", "token-abc"); // cache hit ch1 -> 404 -> evict -> re-resolve -> ch2 -> success

        Assert.That(channelsListCalls, Is.EqualTo(2));
        Assert.That(ch1MessagesCalls, Is.EqualTo(2));
        Assert.That(requests.Last().RequestUri!.AbsolutePath, Is.EqualTo("/api/moderation/channels/ch2/messages"));
    }

    [Test]
    public async Task GetChatRoomMessages_PersistentNotFoundAfterReresolve_ReturnsEmptyArrayWithNoFurtherRetry()
    {
        int channelsListCalls = 0;
        int ch1MessagesCalls = 0;

        var (factory, _) = CreateRoutingFactory(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/moderation/channels")
            {
                channelsListCalls++;
                return JsonResponse(HttpStatusCode.OK, """[ { "id": "ch1", "name": "W3C Lounge", "type": 0 } ]""");
            }

            if (path == "/api/moderation/channels/ch1/messages")
            {
                ch1MessagesCalls++;
                // First call seeds the cache; every call after that 404s (channel id is persistently
                // gone, even after a fresh re-resolve returns the same stale id).
                return ch1MessagesCalls == 1
                    ? JsonResponse(HttpStatusCode.OK, """{ "messages": [], "nextBeforeSeq": null }""")
                    : JsonResponse(HttpStatusCode.NotFound, "not found");
            }

            throw new InvalidOperationException($"Unexpected request path: {path}");
        });

        var client = new ChatServiceClient(factory.Object);

        await client.GetChatRoomMessages("W3C Lounge", "token-abc"); // seeds cache -> ch1

        var messages = await client.GetChatRoomMessages("W3C Lounge", "token-abc");
        // cache hit ch1 -> 404 -> evict -> re-resolve (channels list #2, still ch1) -> retry -> 404 again -> [] (bounded, no 3rd attempt)

        Assert.That(messages, Is.Empty);
        Assert.That(channelsListCalls, Is.EqualTo(2));
        Assert.That(ch1MessagesCalls, Is.EqualTo(3));
    }

    [Test]
    public void GetChatRoomMessages_MessagesFetchNonNotFoundError_PropagatesWithoutRetry()
    {
        const string channelsJson = """[ { "id": "ch1", "name": "W3C Lounge", "type": 0 } ]""";

        var (factory, requests) = CreateRoutingFactory(request =>
            request.RequestUri!.AbsolutePath.Contains("/messages")
                ? JsonResponse(HttpStatusCode.ServiceUnavailable, "unavailable")
                : JsonResponse(HttpStatusCode.OK, channelsJson));

        var client = new ChatServiceClient(factory.Object);

        var exception = Assert.ThrowsAsync<HttpRequestException>(async () => await client.GetChatRoomMessages("W3C Lounge", "token-abc"));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(requests, Has.Count.EqualTo(2)); // channels list + exactly one messages attempt -- no retry loop
    }

    [Test]
    public async Task GetChatRoomMessages_NonPublicNameCollision_NotResolved()
    {
        const string channelsJson = """[ { "id": "ch1", "name": "W3C Lounge", "type": 1 } ]"""; // SemiPublic

        var (factory, requests) = CreateRoutingFactory(_ => JsonResponse(HttpStatusCode.OK, channelsJson));

        var client = new ChatServiceClient(factory.Object);

        var messages = await client.GetChatRoomMessages("W3C Lounge", "token-abc");

        Assert.That(messages, Is.Empty);
        Assert.That(requests, Has.Count.EqualTo(1));
        Assert.That(requests[0].RequestUri!.AbsolutePath, Is.EqualTo("/api/moderation/channels"));
    }

    [TestCase(true)]  // Public row listed first
    [TestCase(false)] // SemiPublic row listed first
    public async Task GetChatRoomMessages_PublicAndNonPublicNameCollision_PublicChannelIdWins(bool publicFirst)
    {
        const string publicRow = """{ "id": "ch-public", "name": "W3C Lounge", "type": 0 }""";
        const string semiPublicRow = """{ "id": "ch-semi", "name": "W3C Lounge", "type": 1 }""";
        string channelsJson = publicFirst
            ? $"[ {publicRow}, {semiPublicRow} ]"
            : $"[ {semiPublicRow}, {publicRow} ]";
        const string messagesJson = """{ "messages": [], "nextBeforeSeq": null }""";

        var (factory, requests) = CreateRoutingFactory(request =>
            request.RequestUri!.AbsolutePath.Contains("/messages")
                ? JsonResponse(HttpStatusCode.OK, messagesJson)
                : JsonResponse(HttpStatusCode.OK, channelsJson));

        var client = new ChatServiceClient(factory.Object);

        await client.GetChatRoomMessages("W3C Lounge", "token-abc");

        // Regardless of list ordering, the Public row's channelId is the one used to fetch
        // messages -- the SemiPublic row sharing the same name must never win the resolution.
        Assert.That(requests, Has.Count.EqualTo(2));
        Assert.That(requests[1].RequestUri!.PathAndQuery, Is.EqualTo("/api/moderation/channels/ch-public/messages?limit=100"));
    }

    [Test]
    public async Task GetChatRoomMessages_MalformedPublicRowWithNullName_SkippedWithoutBreakingOtherRooms()
    {
        const string channelsJson = """
        [
          { "id": "ch-bad", "name": null, "type": 0 },
          { "id": "ch1", "name": "W3C Lounge", "type": 0 }
        ]
        """;
        const string messagesJson = """{ "messages": [], "nextBeforeSeq": null }""";

        var (factory, requests) = CreateRoutingFactory(request =>
            request.RequestUri!.AbsolutePath.Contains("/messages")
                ? JsonResponse(HttpStatusCode.OK, messagesJson)
                : JsonResponse(HttpStatusCode.OK, channelsJson));

        var client = new ChatServiceClient(factory.Object);

        // A single malformed Public row (null name) must not throw and must not poison the cache
        // build for the other, valid Public row also present in the same response.
        var messages = await client.GetChatRoomMessages("W3C Lounge", "token-abc");

        Assert.That(messages, Is.Empty);
        Assert.That(requests, Has.Count.EqualTo(2));
        Assert.That(requests[1].RequestUri!.PathAndQuery, Is.EqualTo("/api/moderation/channels/ch1/messages?limit=100"));
    }

    [Test]
    public async Task GetChatRoomMessages_NullMessagesArrayInPage_ReturnsEmptyArrayWithoutThrowing()
    {
        const string channelsJson = """[ { "id": "ch1", "name": "W3C Lounge", "type": 0 } ]""";
        const string messagesJson = """{ "messages": null, "nextBeforeSeq": null }""";

        var (factory, _) = CreateRoutingFactory(request =>
            request.RequestUri!.AbsolutePath.Contains("/messages")
                ? JsonResponse(HttpStatusCode.OK, messagesJson)
                : JsonResponse(HttpStatusCode.OK, channelsJson));

        var client = new ChatServiceClient(factory.Object);

        var messages = await client.GetChatRoomMessages("W3C Lounge", "token-abc");

        Assert.That(messages, Is.Empty);
    }
}
