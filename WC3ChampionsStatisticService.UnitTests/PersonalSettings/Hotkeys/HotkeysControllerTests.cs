using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.PersonalSettings.Hotkeys;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.PersonalSettings.Hotkeys;

[TestFixture]
public class HotkeysControllerTests
{
    private const string Tag = "Peter#1234";
    private const string RawWithPcKey = "[Reforged]\nhudscale=100\n[Misc]\nreswidth=1920\n";

    private Mock<IHotkeySetupRepository> _repo;
    private FakeTimeProvider _clock;
    private Mock<ILogger<HotkeysController>> _logger;
    private HotkeysController _controller;

    [SetUp]
    public void Setup()
    {
        _repo = new Mock<IHotkeySetupRepository>(MockBehavior.Strict);
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
        _logger = new Mock<ILogger<HotkeysController>>();
        _controller = new HotkeysController(_repo.Object, _clock, _logger.Object);
    }

    private static PutHotkeySetupRequest Request(int mode = 1, string customKeys = "[a]\nHotkey=Q", string prefs = RawWithPcKey, long? expected = null) =>
        new() { Mode = mode, CustomKeys = customKeys, Preferences = prefs, ExpectedRevision = expected };

    private static HotkeySetup Stored(long revision, int mode = 1, string customKeys = "[a]\nHotkey=Q", string prefs = RawWithPcKey) =>
        new()
        {
            Id = Tag,
            Mode = mode,
            CustomKeys = customKeys,
            PreferenceSettings = SyncedPreferences.Filter(prefs),
            Revision = revision,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };

    private static T Body<T>(IActionResult result, int status)
    {
        var obj = (ObjectResult)result;
        obj.StatusCode.Should().Be(status);
        return (T)obj.Value;
    }

    [Test]
    public async Task Get_Absent_Returns404()
    {
        _repo.Setup(r => r.Load(Tag)).ReturnsAsync((HotkeySetup)null);

        (await _controller.Get(Tag)).Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task Get_Present_ReturnsDtoWithPreferenceSettings()
    {
        var stored = Stored(3);
        _repo.Setup(r => r.Load(Tag)).ReturnsAsync(stored);

        var dto = Body<HotkeySetupDto>(await _controller.Get(Tag), 200);

        dto.Should().Be(new HotkeySetupDto(1, stored.CustomKeys, stored.PreferenceSettings, 3, DateTimeOffset.UnixEpoch));
    }

    [Test]
    public void Get_Dto_SerialisesCamelCaseWithIsoUpdatedAt()
    {
        var json = JsonSerializer.Serialize(
            new HotkeySetupDto(1, "k", "p", 3, DateTimeOffset.UnixEpoch), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Be("{\"mode\":1,\"customKeys\":\"k\",\"preferenceSettings\":\"p\",\"revision\":3,\"updatedAt\":\"1970-01-01T00:00:00+00:00\"}");
    }

    [Test]
    public async Task Put_Invalid_Returns400WithReason()
    {
        var body = Body<HotkeyErrorResponse>(await _controller.Put(Tag, Request(mode: 5)), 400);

        body.Error.Should().Be("invalid_mode");
        _repo.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Put_StoresOnlyFilteredPreferences()
    {
        HotkeySetup saved = null;
        _repo.Setup(r => r.Load(Tag)).ReturnsAsync((HotkeySetup)null);
        _repo.Setup(r => r.TryCreate(It.IsAny<HotkeySetup>())).Callback<HotkeySetup>(s => saved = s).ReturnsAsync(true);

        await _controller.Put(Tag, Request());

        saved.PreferenceSettings.Should().NotContain("reswidth");
        saved.PreferenceSettings.Should().Contain("hudscale=100");
        saved.PreferenceSettings.Should().Be(SyncedPreferences.Filter(RawWithPcKey));
    }

    [Test]
    public void Request_ExtraJsonFields_CannotInjectPreferenceSettings()
    {
        var request = JsonSerializer.Deserialize<PutHotkeySetupRequest>(
            "{\"mode\":1,\"customKeys\":\"\",\"preferences\":\"\",\"preferenceSettings\":\"[Evil]\\nx=1\",\"revision\":99}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        typeof(PutHotkeySetupRequest).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo("Mode", "CustomKeys", "Preferences", "ExpectedRevision");
        request.Preferences.Should().BeEmpty();
    }

    [Test]
    public async Task Put_Unchanged_ReturnsCurrentRevisionWithoutWrite()
    {
        _repo.Setup(r => r.Load(Tag)).ReturnsAsync(Stored(4));

        var body = Body<HotkeyRevisionResponse>(await _controller.Put(Tag, Request(expected: 2)), 200);

        body.Revision.Should().Be(4);
        _repo.Verify(r => r.TryCreate(It.IsAny<HotkeySetup>()), Times.Never);
        _repo.Verify(r => r.TryReplace(It.IsAny<HotkeySetup>(), It.IsAny<long>()), Times.Never);
    }

    [Test]
    public async Task Put_OnlyNonSyncedKeysChanged_IsNoOp()
    {
        _repo.Setup(r => r.Load(Tag)).ReturnsAsync(Stored(4));

        var body = Body<HotkeyRevisionResponse>(
            await _controller.Put(Tag, Request(prefs: "[Reforged]\nhudscale=100\n[Misc]\nreswidth=800\n", expected: 4)), 200);

        body.Revision.Should().Be(4);
        _repo.Verify(r => r.TryReplace(It.IsAny<HotkeySetup>(), It.IsAny<long>()), Times.Never);
    }

    [Test]
    public async Task Put_OnlyLineEndingsAndTrailingWhitespaceDiffer_IsNoOp()
    {
        _repo.Setup(r => r.Load(Tag)).ReturnsAsync(Stored(4, customKeys: "[a]\nHotkey=Q"));

        var body = Body<HotkeyRevisionResponse>(
            await _controller.Put(Tag, Request(customKeys: "[a]  \r\nHotkey=Q\r\n\r\n", expected: 4)), 200);

        body.Revision.Should().Be(4);
        _repo.Verify(r => r.TryReplace(It.IsAny<HotkeySetup>(), It.IsAny<long>()), Times.Never);
    }

    [Test]
    public async Task Put_Create_WhenAbsent_Returns200Revision1()
    {
        HotkeySetup saved = null;
        _repo.Setup(r => r.Load(Tag)).ReturnsAsync((HotkeySetup)null);
        _repo.Setup(r => r.TryCreate(It.IsAny<HotkeySetup>())).Callback<HotkeySetup>(s => saved = s).ReturnsAsync(true);

        var body = Body<HotkeyRevisionResponse>(await _controller.Put(Tag, Request()), 200);

        body.Revision.Should().Be(1);
        saved.Id.Should().Be(Tag);
        saved.Revision.Should().Be(1);
        saved.UpdatedAt.Should().Be(_clock.GetUtcNow());
    }

    [Test]
    public async Task Put_Create_WhenExists_Returns409WithCurrentRevision()
    {
        _repo.SetupSequence(r => r.Load(Tag)).ReturnsAsync((HotkeySetup)null).ReturnsAsync(Stored(7, mode: 2));
        _repo.Setup(r => r.TryCreate(It.IsAny<HotkeySetup>())).ReturnsAsync(false);

        var body = Body<HotkeyRevisionResponse>(await _controller.Put(Tag, Request()), 409);

        body.Revision.Should().Be(7);
    }

    [Test]
    public async Task Put_Replace_Matching_Returns200WithExpectedPlusOne()
    {
        HotkeySetup saved = null;
        _repo.Setup(r => r.Load(Tag)).ReturnsAsync(Stored(4, mode: 2));
        _repo.Setup(r => r.TryReplace(It.IsAny<HotkeySetup>(), 4)).Callback<HotkeySetup, long>((s, _) => saved = s).ReturnsAsync(true);

        var body = Body<HotkeyRevisionResponse>(await _controller.Put(Tag, Request(expected: 4)), 200);

        body.Revision.Should().Be(5);
        saved.Revision.Should().Be(5);
        saved.UpdatedAt.Should().Be(_clock.GetUtcNow());
    }

    [Test]
    public async Task Put_Replace_Stale_Returns409WithCurrentRevision()
    {
        _repo.Setup(r => r.Load(Tag)).ReturnsAsync(Stored(6, mode: 2));
        _repo.Setup(r => r.TryReplace(It.IsAny<HotkeySetup>(), 4)).ReturnsAsync(false);

        var body = Body<HotkeyRevisionResponse>(await _controller.Put(Tag, Request(expected: 4)), 409);

        body.Revision.Should().Be(6);
    }

    [Test]
    public async Task Put_Replace_WhenAbsent_Returns409WithNullRevision()
    {
        _repo.Setup(r => r.Load(Tag)).ReturnsAsync((HotkeySetup)null);
        _repo.Setup(r => r.TryReplace(It.IsAny<HotkeySetup>(), 4)).ReturnsAsync(false);

        var body = Body<HotkeyRevisionResponse>(await _controller.Put(Tag, Request(expected: 4)), 409);

        body.Revision.Should().BeNull();
    }

    [Test]
    public async Task Put_OnlyCustomKeysDiffer_Replaces()
    {
        _repo.Setup(r => r.Load(Tag)).ReturnsAsync(Stored(4));
        _repo.Setup(r => r.TryReplace(It.IsAny<HotkeySetup>(), 4)).ReturnsAsync(true);

        var body = Body<HotkeyRevisionResponse>(await _controller.Put(Tag, Request(customKeys: "[a]\nHotkey=W", expected: 4)), 200);

        body.Revision.Should().Be(5);
        _repo.Verify(r => r.TryReplace(It.Is<HotkeySetup>(s => s.CustomKeys == "[a]\nHotkey=W" && s.Revision == 5), 4), Times.Once);
    }

    [Test]
    public async Task Put_OnlySyncedPreferenceDiffers_Replaces()
    {
        _repo.Setup(r => r.Load(Tag)).ReturnsAsync(Stored(4));
        _repo.Setup(r => r.TryReplace(It.IsAny<HotkeySetup>(), 4)).ReturnsAsync(true);

        var body = Body<HotkeyRevisionResponse>(
            await _controller.Put(Tag, Request(prefs: "[Reforged]\nhudscale=120\n[Misc]\nreswidth=1920\n", expected: 4)), 200);

        body.Revision.Should().Be(5);
        _repo.Verify(r => r.TryReplace(It.Is<HotkeySetup>(s => s.PreferenceSettings.Contains("hudscale=120")), 4), Times.Once);
    }

    [Test]
    public async Task Put_NullCustomKeys_StoresNull()
    {
        HotkeySetup saved = null;
        _repo.Setup(r => r.Load(Tag)).ReturnsAsync((HotkeySetup)null);
        _repo.Setup(r => r.TryCreate(It.IsAny<HotkeySetup>())).Callback<HotkeySetup>(s => saved = s).ReturnsAsync(true);

        await _controller.Put(Tag, Request(customKeys: null));

        saved.CustomKeys.Should().BeNull();
    }

    [Test]
    public async Task Get_NullCustomKeys_ReturnsNullInDto()
    {
        _repo.Setup(r => r.Load(Tag)).ReturnsAsync(Stored(3, customKeys: null));

        Body<HotkeySetupDto>(await _controller.Get(Tag), 200).CustomKeys.Should().BeNull();
    }

    [Test]
    public async Task Put_NullCustomKeysAgainstStoredEmpty_IsNoOp()
    {
        _repo.Setup(r => r.Load(Tag)).ReturnsAsync(Stored(4, customKeys: ""));

        var body = Body<HotkeyRevisionResponse>(await _controller.Put(Tag, Request(customKeys: null, expected: 4)), 200);

        body.Revision.Should().Be(4);
    }

    [Test]
    public async Task Put_NullBody_Returns400InvalidBody()
    {
        Body<HotkeyErrorResponse>(await _controller.Put(Tag, null), 400).Error.Should().Be("invalid_body");
        _repo.VerifyNoOtherCalls();
    }

    [TestCase(-1L)]
    [TestCase(long.MaxValue)]
    public async Task Put_OutOfRangeExpectedRevision_Returns400(long expected)
    {
        Body<HotkeyErrorResponse>(await _controller.Put(Tag, Request(expected: expected)), 400).Error.Should().Be("invalid_expected_revision");
        _repo.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Put_Replace_RacingIdenticalUpload_Returns200WithWinnersRevision()
    {
        _repo.SetupSequence(r => r.Load(Tag)).ReturnsAsync(Stored(4, mode: 2)).ReturnsAsync(Stored(5));
        _repo.Setup(r => r.TryReplace(It.IsAny<HotkeySetup>(), 4)).ReturnsAsync(false);

        var body = Body<HotkeyRevisionResponse>(await _controller.Put(Tag, Request(expected: 4)), 200);

        body.Revision.Should().Be(5);
    }

    [Test]
    public async Task Put_Create_RacingIdenticalUpload_Returns200WithWinnersRevision()
    {
        _repo.SetupSequence(r => r.Load(Tag)).ReturnsAsync((HotkeySetup)null).ReturnsAsync(Stored(1));
        _repo.Setup(r => r.TryCreate(It.IsAny<HotkeySetup>())).ReturnsAsync(false);

        var body = Body<HotkeyRevisionResponse>(await _controller.Put(Tag, Request()), 200);

        body.Revision.Should().Be(1);
    }

    [Test]
    public async Task Put_LogsOnlyBattleTagAndOutcome()
    {
        _repo.Setup(r => r.Load(Tag)).ReturnsAsync(Stored(4));

        await _controller.Put(Tag, Request(expected: 4));

        _logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString() == $"Hotkey setup upload for {Tag}: noop"),
            null,
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
    }

    [Test]
    public void BearerFilter_IsNotOrdered_SoItRunsBeforeRateLimit()
    {
        // A filter attribute that does not implement IOrderedFilter gets the default Order 0, so it runs
        // before HotkeysRateLimitAttribute (Order 100) and has already rewritten the battleTag argument.
        typeof(IOrderedFilter).IsAssignableFrom(typeof(BearerCheckIfBattleTagBelongsToAuthAttribute)).Should().BeFalse();
        typeof(IOrderedFilter).IsAssignableFrom(typeof(BearerCheckIfBattleTagBelongsToAuthFilter)).Should().BeFalse();
    }

    [Test]
    public void PutAction_HasSizeLimitRateLimitAndBearerAttributes()
    {
        var put = typeof(HotkeysController).GetMethod(nameof(HotkeysController.Put));

        ((IRequestSizeLimitMetadata)put.GetCustomAttribute<RequestSizeLimitAttribute>()).MaxRequestBodySize.Should().Be(655360);
        put.GetCustomAttribute<HotkeysRateLimitAttribute>().Should().NotBeNull();
        put.GetCustomAttribute<BearerCheckIfBattleTagBelongsToAuthAttribute>().Should().NotBeNull();
        typeof(HotkeysController).GetMethod(nameof(HotkeysController.Get))
            .GetCustomAttribute<BearerCheckIfBattleTagBelongsToAuthAttribute>().Should().NotBeNull();
    }
}
