using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;

namespace WC3ChampionsStatisticService.Tests.Matchups;

// The finished-matches map list (map-names) and the mapName filter of the matches list, end to end on
// MatchRepository + MatchService: current-name modes list and find maps by their current name.
// Per-test Mongo2Go runner (mirrors Friend/FriendRepositoryTests.cs) — deliberately NOT
// IntegrationTestBase, which points at a shared remote Mongo and drops it.
[TestFixture]
public class MatchMapNameFilterTests
{
    private const int Season = 17;
    private const string EchoIslesOldFile = "w3c_EchoIsles_v2.2.w3x";
    private const string EchoIslesFile = "w3c_s17_240104_EchoIsles_v2.2.w3x";
    private const string TwistedMeadowsFile = "w3c_250622_1057_TwistedMeadows_v1.9.w3x";
    // A key kept as Map, with no id and its historical name.
    private const string AutumnLeavesLegacyKey = "autumnleaves";
    private const string LegionTdFile = "Legion_TD_11.0_W3C_TeamOZE.w3x";

    private static readonly CurrentMapNames CurrentNames = new(new Dictionary<int, string>
    {
        [1051] = "Echo Isles v2",
        [5072] = "Legion TD: X3",
    });

    private MongoDbRunner _runner;
    private MatchRepository _repository;

    [SetUp]
    public async Task Setup()
    {
        _runner = MongoDbRunner.Start();
        var mongoClient = new MongoClient(_runner.ConnectionString);
        _repository = new MatchRepository(mongoClient, new OngoingMatchesCache(mongoClient, TestDtoHelper.CreateMockedTracingService().Object));

        await Insert("a", GameMode.GM_1v1, EchoIslesOldFile, 1051, "Echo Isles 2.0");
        await Insert("b", GameMode.GM_1v1, EchoIslesFile, 1051, "Echo Isles v2");
        await Insert("c", GameMode.GM_1v1, EchoIslesFile, 1051, "Echo Isles v2");
        // Id 6 is unknown to the matchmaking catalog in these tests, so it keeps its stored name.
        await Insert("d", GameMode.GM_1v1, TwistedMeadowsFile, 6, "Twisted Meadows");
        await Insert("e", GameMode.GM_1v1, AutumnLeavesLegacyKey, null, "Autumn Leaves");
        await Insert("f", GameMode.GM_1v1, "unnamed", null, null);
        await Insert("g", GameMode.GM_LEGION_1v1_x20, LegionTdFile, 5072, "Legion TD 11.0");
        await Insert("h", GameMode.GM_1v1, EchoIslesFile, 1051, "Echo Isles v2", season: Season + 1);
    }

    [TearDown]
    public void TearDown()
    {
        _runner.Dispose();
    }

    private Task Insert(string matchId, GameMode gameMode, string map, int? mapId, string mapName, int season = Season)
    {
        return _repository.Insert(new Matchup
        {
            Id = ObjectId.GenerateNewId(),
            MatchId = matchId,
            Season = season,
            GameMode = gameMode,
            Map = map,
            MapId = mapId,
            MapName = mapName,
            EndTime = DateTimeOffset.UtcNow,
        });
    }

    private MatchService CreateService(CurrentMapNames currentMapNames)
    {
        return new MatchService(
            _repository,
            Mock.Of<ICachedDataProvider<List<Matchup>>>(),
            Mock.Of<ICachedDataProvider<CachedLong>>(),
            new InMemoryCachedDataProvider<List<MapIdAndName>>(
                new OptionsWrapper<CacheOptionsFor<List<MapIdAndName>>>(new CacheOptionsFor<List<MapIdAndName>>()),
                new MemoryCache(new MemoryCacheOptions())),
            Mock.Of<IPersonalSettingsRepository>(),
            TestDtoHelper.CreateCurrentMapNameProvider(currentMapNames).Object);
    }

    private async Task<List<string>> MatchIdsFor(MatchService service, GameMode gameMode, string mapName)
    {
        var filter = await service.GetMapNameFilter(Season, gameMode, mapName);
        var matches = await _repository.Load(Season, gameMode, mapName: mapName, mapNameFilter: filter);
        var count = await _repository.Count(Season, gameMode, mapName: mapName, mapNameFilter: filter);
        Assert.That(count, Is.EqualTo(matches.Count));
        return matches.Select(m => m.MatchId).ToList();
    }

    [Test]
    public async Task LoadMapIdsAndNames_GroupsBySeasonModeIdAndName()
    {
        var pairs = await _repository.LoadMapIdsAndNames(Season, GameMode.GM_1v1);

        Assert.That(pairs.Select(p => (p.MapId, p.MapName)), Is.EquivalentTo(new (int?, string)[]
        {
            (1051, "Echo Isles 2.0"),
            (1051, "Echo Isles v2"),
            (6, "Twisted Meadows"),
            (null, "Autumn Leaves"),
        }));
    }

    [Test]
    public async Task GetMapNames_CurrentNameMode_ListsCurrentNamesOnce()
    {
        var names = await CreateService(CurrentNames).GetMapNames(Season, GameMode.GM_1v1);

        Assert.That(names, Is.EqualTo(new[] { "Autumn Leaves", "Echo Isles v2", "Twisted Meadows" }));
    }

    [Test]
    public async Task GetMapNames_WithoutCurrentNames_ListsStoredNames()
    {
        var names = await CreateService(CurrentMapNames.Empty).GetMapNames(Season, GameMode.GM_1v1);

        Assert.That(names, Is.EqualTo(new[] { "Autumn Leaves", "Echo Isles 2.0", "Echo Isles v2", "Twisted Meadows" }));
    }

    [Test]
    public async Task GetMapNames_OtherMode_ListsStoredNames()
    {
        var names = await CreateService(CurrentNames).GetMapNames(Season, GameMode.GM_LEGION_1v1_x20);

        Assert.That(names, Is.EqualTo(new[] { "Legion TD 11.0" }));
    }

    [Test]
    public async Task Filter_CurrentName_FindsGamesStoredUnderEveryNameOfThatId()
    {
        var matchIds = await MatchIdsFor(CreateService(CurrentNames), GameMode.GM_1v1, "Echo Isles v2");

        Assert.That(matchIds, Is.EquivalentTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public async Task Filter_OldNameOfARenamedId_FindsNothing()
    {
        var matchIds = await MatchIdsFor(CreateService(CurrentNames), GameMode.GM_1v1, "Echo Isles 2.0");

        Assert.That(matchIds, Is.Empty);
    }

    [Test]
    public async Task Filter_NoIdOrUnknownId_MatchesTheStoredName()
    {
        var service = CreateService(CurrentNames);

        Assert.That(await MatchIdsFor(service, GameMode.GM_1v1, "Autumn Leaves"), Is.EqualTo(new[] { "e" }));
        Assert.That(await MatchIdsFor(service, GameMode.GM_1v1, "Twisted Meadows"), Is.EqualTo(new[] { "d" }));
    }

    [Test]
    public async Task Filter_WithoutCurrentNames_MatchesTheStoredName()
    {
        var service = CreateService(CurrentMapNames.Empty);

        Assert.That(await MatchIdsFor(service, GameMode.GM_1v1, "Echo Isles 2.0"), Is.EqualTo(new[] { "a" }));
        Assert.That(await MatchIdsFor(service, GameMode.GM_1v1, "Echo Isles v2"), Is.EquivalentTo(new[] { "b", "c" }));
    }

    [Test]
    public async Task Filter_OtherMode_IsTheStoredName()
    {
        var service = CreateService(CurrentNames);

        Assert.That(await service.GetMapNameFilter(Season, GameMode.GM_LEGION_1v1_x20, "Legion TD 11.0"), Is.Null);
        Assert.That(await MatchIdsFor(service, GameMode.GM_LEGION_1v1_x20, "Legion TD 11.0"), Is.EqualTo(new[] { "g" }));
        Assert.That(await MatchIdsFor(service, GameMode.GM_LEGION_1v1_x20, "Legion TD: X3"), Is.Empty);
    }

    [Test]
    [TestCase("Overall")]
    [TestCase("")]
    [TestCase(null)]
    public async Task Filter_OverallOrEmpty_IsNoFilter(string mapName)
    {
        var service = CreateService(CurrentNames);

        Assert.That(await service.GetMapNameFilter(Season, GameMode.GM_1v1, mapName), Is.Null);
        Assert.That(await MatchIdsFor(service, GameMode.GM_1v1, mapName), Has.Count.EqualTo(6));
    }
}
