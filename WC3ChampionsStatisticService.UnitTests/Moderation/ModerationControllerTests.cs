using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Admin.Moderation;
using W3C.Domain.ChatService;
using W3ChampionsStatisticService.Moderation;

namespace WC3ChampionsStatisticService.Tests.Moderation;

/// <summary>
/// Regression tests for <see cref="ModerationController"/>'s wire shape and lounge-mute
/// proxying, enabled by extracting <see cref="IChatServiceClient"/> so the controller can be
/// unit-tested against a Moq mock instead of a live/HTTP-backed <see cref="ChatServiceClient"/>.
/// </summary>
[TestFixture]
public class ModerationControllerTests
{
    private Mock<IChatServiceClient> _chatServiceClient;

    [SetUp]
    public void SetUp()
    {
        _chatServiceClient = new Mock<IChatServiceClient>();
    }

    private ModerationController CreateController() => new(_chatServiceClient.Object);

    [Test]
    public async Task GetChatRoomMessages_ReturnsOkWithClientResult()
    {
        ChatMessage[] clientResult =
        [
            new ChatMessage { Id = "msg1", Message = "gl hf", Time = "2026-07-03T21:10:00.000Z", BattleTag = "Peter#123" },
        ];
        _chatServiceClient.Setup(c => c.GetChatRoomMessages("w3c lounge", "token-abc")).ReturnsAsync(clientResult);

        var result = await CreateController().GetChatRoomMessages("w3c lounge", "token-abc");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(((OkObjectResult)result).Value, Is.SameAs(clientResult));
    }

    [Test]
    public async Task GetChatRoomMessages_WireShape_LegacyKeysExact()
    {
        ChatMessage[] clientResult =
        [
            new ChatMessage { Id = "msg1", Message = "gl hf", Time = "2026-07-03T21:10:00.000Z", BattleTag = "Peter#123" },
        ];
        _chatServiceClient.Setup(c => c.GetChatRoomMessages("w3c lounge", "token-abc")).ReturnsAsync(clientResult);

        var result = await CreateController().GetChatRoomMessages("w3c lounge", "token-abc");
        var payload = ((OkObjectResult)result).Value;

        // ASP.NET Core's actual runtime serializer for controller responses is System.Text.Json
        // (JsonSerializerDefaults.Web), not the Newtonsoft attributes carried on the ChatMessage
        // DTO itself -- mirrors W1's PlayersControllerTests precedent for pinning wire shape.
        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement[0];

        var propertyNames = new System.Collections.Generic.List<string>();
        foreach (var property in element.EnumerateObject())
        {
            propertyNames.Add(property.Name);
        }
        Assert.That(propertyNames, Is.EquivalentTo(new[] { "id", "message", "time", "battleTag" }));

        Assert.That(element.GetProperty("id").GetString(), Is.EqualTo("msg1"));
        Assert.That(element.GetProperty("message").GetString(), Is.EqualTo("gl hf"));
        Assert.That(element.GetProperty("battleTag").GetString(), Is.EqualTo("Peter#123"));

        // Must be a JS-Date-parsable ISO timestamp.
        string time = element.GetProperty("time").GetString();
        Assert.That(System.DateTimeOffset.Parse(time), Is.EqualTo(System.DateTimeOffset.Parse("2026-07-03T21:10:00.000Z")));
    }

    [Test]
    public async Task GetChatRoomMessages_EmptyFromClient_ReturnsOkEmptyArray()
    {
        _chatServiceClient.Setup(c => c.GetChatRoomMessages("does-not-exist", "token-abc")).ReturnsAsync([]);

        var result = await CreateController().GetChatRoomMessages("does-not-exist", "token-abc");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That((ChatMessage[])((OkObjectResult)result).Value, Is.Empty);
    }

    [Test]
    public void GetChatRoomMessages_ClientThrowsHttpRequestException_Propagates()
    {
        // The controller action does not catch HttpRequestException itself -- mapping it to an
        // HTTP status is pre-existing, out-of-unit-scope global HttpRequestExceptionFilter wiring.
        _chatServiceClient.Setup(c => c.GetChatRoomMessages("w3c lounge", "token-abc"))
            .ThrowsAsync(new HttpRequestException("boom", null, HttpStatusCode.ServiceUnavailable));

        var controller = CreateController();

        var exception = Assert.ThrowsAsync<HttpRequestException>(
            async () => await controller.GetChatRoomMessages("w3c lounge", "token-abc"));
        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }

    [Test]
    public async Task GetChatRoomMessagesPaged_ReturnsOkWithClientResult()
    {
        var historyResult = new ModerationChatHistoryDto
        {
            Messages =
            [
                new ModerationChatMessageDto { Id = "msg1", Seq = 48198, Message = "gl hf", Time = "2026-07-03T21:10:00.000Z", BattleTag = "Peter#123", SenderName = "Peter", Deleted = false, Shadow = false },
            ],
            NextBeforeSeq = 48198,
        };
        _chatServiceClient.Setup(c => c.GetModerationChannelHistory("w3c lounge", 48198L, 50, "token-abc")).ReturnsAsync(historyResult);

        var result = await CreateController().GetChatRoomMessagesPaged("w3c lounge", 48198, 50, "token-abc");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(((OkObjectResult)result).Value, Is.SameAs(historyResult));
    }

    [Test]
    public async Task GetChatRoomMessagesPaged_NoQueryParams_PassesNullsThrough()
    {
        var historyResult = new ModerationChatHistoryDto { Messages = [], NextBeforeSeq = null };
        _chatServiceClient.Setup(c => c.GetModerationChannelHistory("w3c lounge", null, null, "token-abc")).ReturnsAsync(historyResult);

        var result = await CreateController().GetChatRoomMessagesPaged("w3c lounge", null, null, "token-abc");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _chatServiceClient.Verify(c => c.GetModerationChannelHistory("w3c lounge", null, null, "token-abc"), Times.Once);
    }

    [Test]
    public async Task GetChatRoomMessagesPaged_WireShape_IncludesFlagsAndCursor()
    {
        var historyResult = new ModerationChatHistoryDto
        {
            Messages =
            [
                new ModerationChatMessageDto
                {
                    Id = "msg1",
                    Seq = 48199,
                    Message = "buy gold",
                    Time = "2026-07-03T21:10:05.000Z",
                    BattleTag = "Spammer#456",
                    SenderName = "Spammer",
                    Deleted = true,
                    DeletedBy = "mod#1",
                    DeletedAt = "2026-07-03T21:11:00.000Z",
                    Shadow = false,
                },
            ],
            NextBeforeSeq = 48198,
        };
        _chatServiceClient.Setup(c => c.GetModerationChannelHistory("w3c lounge", null, null, "token-abc")).ReturnsAsync(historyResult);

        var result = await CreateController().GetChatRoomMessagesPaged("w3c lounge", null, null, "token-abc");
        var payload = ((OkObjectResult)result).Value;

        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.GetProperty("nextBeforeSeq").GetInt64(), Is.EqualTo(48198));
        var message = doc.RootElement.GetProperty("messages")[0];
        Assert.That(message.GetProperty("seq").GetInt64(), Is.EqualTo(48199));
        Assert.That(message.GetProperty("senderName").GetString(), Is.EqualTo("Spammer"));
        Assert.That(message.GetProperty("deleted").GetBoolean(), Is.True);
        Assert.That(message.GetProperty("deletedBy").GetString(), Is.EqualTo("mod#1"));
        Assert.That(message.GetProperty("shadow").GetBoolean(), Is.False);
    }

    [Test]
    public async Task GetLoungeMutes_DelegatesToClient()
    {
        LoungeMuteResponse[] clientResult =
        [
            new LoungeMuteResponse { battleTag = "Peter#123", endDate = "2026-07-10", insertDate = "2026-07-03" },
        ];
        _chatServiceClient.Setup(c => c.GetLoungeMutes("token-abc")).ReturnsAsync(clientResult);

        var result = await CreateController().GetLoungeMutes("token-abc");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(((OkObjectResult)result).Value, Is.SameAs(clientResult));
        _chatServiceClient.Verify(c => c.GetLoungeMutes("token-abc"), Times.Once);
    }

    [Test]
    public async Task PostLoungeMute_EmptyBattleTag_BadRequest()
    {
        var loungeMute = new LoungeMute { battleTag = "", endDate = "2026-07-10" };

        var result = await CreateController().PostLoungeMute(loungeMute, "token-abc");

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        _chatServiceClient.Verify(c => c.PostLoungeMute(It.IsAny<LoungeMute>(), It.IsAny<string>()), Times.Never);
    }
}
