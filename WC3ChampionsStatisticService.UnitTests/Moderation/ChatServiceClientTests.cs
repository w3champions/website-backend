using System;
using System.Collections.Generic;
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
}
