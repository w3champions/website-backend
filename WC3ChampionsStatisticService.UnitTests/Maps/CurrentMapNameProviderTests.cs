using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.Maps;
using WC3ChampionsStatisticService.Tests.Admin;
using static WC3ChampionsStatisticService.Tests.Maps.RecomputeJson;

namespace WC3ChampionsStatisticService.Tests.Maps;

// The real MatchmakingServiceClient over a routing HTTP handler, and the real in-memory cache
// (a fresh MemoryCache per test, with a clock that only moves when the test moves it).
[TestFixture]
public class CurrentMapNameProviderTests
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    private RoutingHandler _handler;
    private FakeClock _clock;
    private CurrentMapNameProvider _provider;

    [SetUp]
    public void SetUp()
    {
        _handler = new RoutingHandler();
        _clock = new FakeClock();
        var cache = new InMemoryCachedDataProvider<CurrentMapNames>(
            new OptionsWrapper<CacheOptionsFor<CurrentMapNames>>(new CacheOptionsFor<CurrentMapNames> { CacheDuration = CacheDuration }),
            new MemoryCache(new MemoryCacheOptions { Clock = _clock }));
        _provider = new CurrentMapNameProvider(new MatchmakingServiceClient(new RecordingHttpClientFactory(_handler)), cache);
    }

    private void AnswerMaps(params object[] maps) =>
        _handler.On(HttpMethod.Get, RoutingHandler.MmMapsPath, HttpStatusCode.OK, MmMaps(maps));

    private int MapRequests => _handler.RequestsFor(HttpMethod.Get, RoutingHandler.MmMapsPath).Count;

    [Test]
    public async Task GetAsync_ReturnsCurrentNamesById()
    {
        AnswerMaps(MmMapWithoutGameMap(1051, "Echo Isles v2"), MmMapWithoutGameMap(44, "Autumn Leaves v2"));

        var names = await _provider.GetAsync();

        Assert.That(names.Resolve(GameMode.GM_1v1, 1051, "Echo Isles 2.0"), Is.EqualTo("Echo Isles v2"));
        Assert.That(names.Resolve(GameMode.GM_1v1, 44, "Autumn Leaves"), Is.EqualTo("Autumn Leaves v2"));
        Assert.That(names.Resolve(GameMode.GM_1v1, 6, "Twisted Meadows"), Is.EqualTo("Twisted Meadows"));
    }

    [Test]
    public async Task GetAsync_WithinCacheDuration_UsesTheCache_ThenFetchesTheRename()
    {
        AnswerMaps(MmMapWithoutGameMap(1051, "Echo Isles 2.0"));
        await _provider.GetAsync();

        AnswerMaps(MmMapWithoutGameMap(1051, "Echo Isles v2"));
        _clock.Advance(CacheDuration - TimeSpan.FromSeconds(1));
        var cached = await _provider.GetAsync();
        _clock.Advance(TimeSpan.FromSeconds(1));
        var renamed = await _provider.GetAsync();

        Assert.That(cached.Resolve(1051, null), Is.EqualTo("Echo Isles 2.0"));
        Assert.That(renamed.Resolve(1051, null), Is.EqualTo("Echo Isles v2"));
        Assert.That(MapRequests, Is.EqualTo(2));
    }

    [Test]
    public async Task GetAsync_MatchmakingError_ReturnsEmpty_CachedForTheFailureDuration()
    {
        _handler.On(HttpMethod.Get, RoutingHandler.MmMapsPath, HttpStatusCode.InternalServerError, "{}");
        var failed = await _provider.GetAsync();

        AnswerMaps(MmMapWithoutGameMap(1051, "Echo Isles v2"));
        _clock.Advance(CurrentMapNameProvider.FailureCacheDuration - TimeSpan.FromSeconds(1));
        var stillFailed = await _provider.GetAsync();
        _clock.Advance(TimeSpan.FromSeconds(1));
        var recovered = await _provider.GetAsync();

        Assert.That(failed.Count, Is.EqualTo(0));
        Assert.That(failed.Resolve(GameMode.GM_1v1, 1051, "Echo Isles 2.0"), Is.EqualTo("Echo Isles 2.0"));
        Assert.That(stillFailed.Count, Is.EqualTo(0));
        Assert.That(recovered.Resolve(1051, null), Is.EqualTo("Echo Isles v2"));
        Assert.That(MapRequests, Is.EqualTo(2));
    }

    [Test]
    public async Task GetAsync_Timeout_ReturnsEmpty_WithoutAskingAgainRightAway()
    {
        _handler.OnThrow(HttpMethod.Get, RoutingHandler.MmMapsPath, new TaskCanceledException("timeout"));

        var first = await _provider.GetAsync();
        var second = await _provider.GetAsync();

        Assert.That(first.Count, Is.EqualTo(0));
        Assert.That(second.Count, Is.EqualTo(0));
        Assert.That(MapRequests, Is.EqualTo(1));
    }

    [Test]
    public async Task GetAsync_SkipsMapsWithoutName()
    {
        AnswerMaps(MmMapWithoutGameMap(1051, "Echo Isles v2"), MmMapWithoutGameMap(44, ""), MmMapWithoutGameMap(12, null));

        var names = await _provider.GetAsync();

        Assert.That(names.Count, Is.EqualTo(1));
        Assert.That(names.Resolve(44, "Autumn Leaves"), Is.EqualTo("Autumn Leaves"));
        Assert.That(names.HasCurrentName(12), Is.False);
    }

    private sealed class FakeClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
