using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.Friends;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Friend;

[TestFixture]
public class ChatRelationshipsControllerTests
{
    private Mock<IFriendRepository> _friendRepository;
    private ChatRelationshipsController _controller;

    [SetUp]
    public void SetUp()
    {
        _friendRepository = new Mock<IFriendRepository>();
        _controller = new ChatRelationshipsController(_friendRepository.Object);
    }

    [Test]
    public async Task KnownPlayer_ReturnsFriendsAndBlocked()
    {
        var friendlist = new Friendlist("peter#123")
        {
            Friends = new List<string> { "A#1", "B#2" },
            BlockedBattleTags = new List<string> { "C#3" }
        };
        _friendRepository.Setup(r => r.LoadFriendlistOrNull("peter#123")).ReturnsAsync(friendlist);

        var result = await _controller.GetChatRelationships("peter#123");

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        var dto = ok.Value as ChatRelationshipsDto;
        Assert.That(dto, Is.Not.Null);
        CollectionAssert.AreEquivalent(new List<string> { "A#1", "B#2" }, dto.Friends);
        CollectionAssert.AreEquivalent(new List<string> { "C#3" }, dto.Blocked);
    }

    [Test]
    public async Task UnknownPlayer_Returns200EmptyArrays()
    {
        // Chat treats a non-2xx as a fetch failure and would fail closed for every legitimately
        // unknown-to-website-backend battleTag, so this must NEVER be a 404.
        _friendRepository.Setup(r => r.LoadFriendlistOrNull("ghost#0000")).ReturnsAsync((Friendlist)null);

        var result = await _controller.GetChatRelationships("ghost#0000");

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        var dto = ((OkObjectResult)result).Value as ChatRelationshipsDto;
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto.Friends, Is.Not.Null);
        Assert.That(dto.Friends, Is.Empty);
        Assert.That(dto.Blocked, Is.Not.Null);
        Assert.That(dto.Blocked, Is.Empty);
    }

    [Test]
    public async Task NullListsOnLegacyDoc_CoercedToEmpty()
    {
        var friendlist = new Friendlist("legacy#1") { Friends = null, BlockedBattleTags = null };
        _friendRepository.Setup(r => r.LoadFriendlistOrNull("legacy#1")).ReturnsAsync(friendlist);

        var result = await _controller.GetChatRelationships("legacy#1");

        var dto = ((OkObjectResult)result).Value as ChatRelationshipsDto;
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto.Friends, Is.Not.Null);
        Assert.That(dto.Friends, Is.Empty);
        Assert.That(dto.Blocked, Is.Not.Null);
        Assert.That(dto.Blocked, Is.Empty);
    }

    [Test]
    public async Task NeverWrites()
    {
        _friendRepository.Setup(r => r.LoadFriendlistOrNull(It.IsAny<string>())).ReturnsAsync((Friendlist)null);

        await _controller.GetChatRelationships("someone#1");

        _friendRepository.Verify(r => r.LoadFriendlistOrNull("someone#1"), Times.Once);
        _friendRepository.VerifyNoOtherCalls();
    }

    [Test]
    public void WireShape_LowercaseKeysAlwaysPresent_NoPascalCaseRegression()
    {
        // Wire-shape pin: C5's WebsiteBackendRelationshipSource (chat-service) Newtonsoft-
        // deserializes expecting exact lowercase "friends"/"blocked" keys, both always present and
        // non-null. A casing regression here is a malformed 200 that makes chat-service fail
        // closed platform-wide. Program.cs has no JSON customization on AddControllers(), so
        // ASP.NET Core's runtime serialization is equivalent to JsonSerializerDefaults.Web.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var populated = new ChatRelationshipsDto(new List<string> { "A#1", "B#2" }, new List<string> { "C#3" });
        var populatedJson = JsonSerializer.Serialize(populated, options);
        Assert.That(populatedJson, Is.EqualTo("{\"friends\":[\"A#1\",\"B#2\"],\"blocked\":[\"C#3\"]}"));
        Assert.That(populatedJson, Does.Not.Contain("\"Friends\""));
        Assert.That(populatedJson, Does.Not.Contain("\"Blocked\""));

        var empty = new ChatRelationshipsDto(new List<string>(), new List<string>());
        var emptyJson = JsonSerializer.Serialize(empty, options);
        Assert.That(emptyJson, Is.EqualTo("{\"friends\":[],\"blocked\":[]}"));
        Assert.That(emptyJson, Does.Not.Contain("\"Friends\""));
        Assert.That(emptyJson, Does.Not.Contain("\"Blocked\""));
    }

    [Test]
    public void Action_CarriesChatServiceSecretAuthAttribute()
    {
        // Pins that the endpoint can never accidentally ship unauthenticated (C7 uses the same
        // reflection-pin idiom).
        var method = typeof(ChatRelationshipsController).GetMethod("GetChatRelationships");

        Assert.That(method, Is.Not.Null);
        var attribute = method.GetCustomAttribute<ChatServiceSecretAuthAttribute>();
        Assert.That(attribute, Is.Not.Null);
    }
}
