using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.Admin.ReplayExport;
using W3ChampionsStatisticService.Matches;

namespace WC3ChampionsStatisticService.Tests.Admin.ReplayExport;

/// <summary>
/// Runs the export filter against an isolated in-memory mongod (Mongo2Go), not the shared remote Mongo.
/// </summary>
[TestFixture]
public class ReplayExportQueryTests
{
    private static readonly DateTime Patch1Start = new(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Patch2Start = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime RollbackStart = new(2026, 10, 8, 22, 0, 0, DateTimeKind.Utc);

    private static readonly List<Patch> Patches =
    [
        new() { PatchId = "a", Version = "3.0.0", StartDate = Patch1Start },
        new() { PatchId = "b", Version = "3.0.1", StartDate = Patch2Start },
        new() { PatchId = "c", Version = "3.0.0", StartDate = RollbackStart },
    ];

    private MongoDbRunner _runner;
    private IMongoCollection<Matchup> _matches;
    private List<ReplayExportPatchOption> _patchOptions;

    [SetUp]
    public void SetUp()
    {
        _runner = MongoDbRunner.Start();
        _matches = new MongoClient(_runner.ConnectionString)
            .GetDatabase("W3Champions-Statistic-Service")
            .GetCollection<Matchup>("Matchup");
        _patchOptions = ReplayExportQuery.PatchOptions(Patches);
    }

    [TearDown]
    public void TearDown()
    {
        _runner.Dispose();
    }

    [Test]
    public void PatchOptions_RolledBackVersionHasTwoWindows()
    {
        var oldPatch = _patchOptions.Single(p => p.Version == "3.0.0");

        Assert.AreEqual(2, oldPatch.Windows.Count);
        Assert.AreEqual(Patch2Start, oldPatch.Windows[0].To);
        Assert.AreEqual(RollbackStart, oldPatch.Windows[1].From);
        Assert.IsNull(oldPatch.Windows[1].To);
        Assert.AreEqual("3.0.0", _patchOptions[0].Version, "The live patch comes first");
    }

    [Test]
    public async Task Patch_SelectsGamesFromEveryWindowOfTheVersion()
    {
        await Insert(
            Game("before", Patch1Start.AddDays(-1)),
            Game("first-window", Patch1Start.AddDays(1)),
            Game("other-patch", Patch2Start.AddHours(5)),
            Game("after-rollback", RollbackStart.AddHours(1)));

        CollectionAssert.AreEquivalent(
            new[] { "first-window", "after-rollback" },
            await Find(new ReplayExportFilter { Patch = "3.0.0" }));
        CollectionAssert.AreEquivalent(
            new[] { "other-patch" },
            await Find(new ReplayExportFilter { Patch = "3.0.1" }));
        CollectionAssert.IsEmpty(await Find(new ReplayExportFilter { Patch = "9.9.9" }));
    }

    [Test]
    public async Task MmrRange_EveryPlayerOrAnyPlayer()
    {
        await Insert(
            Game("both-in", Patch2Start.AddHours(1), mmr1: 2000, mmr2: 2100),
            Game("one-in", Patch2Start.AddHours(2), mmr1: 2000, mmr2: 1500),
            Game("none-in", Patch2Start.AddHours(3), mmr1: 1200, mmr2: 1500));

        var every = new ReplayExportFilter { MinMmr = 1900, MaxMmr = 2200 };
        var any = new ReplayExportFilter { MinMmr = 1900, MaxMmr = 2200, MmrMode = ReplayExportMmrMode.AnyPlayer };

        CollectionAssert.AreEquivalent(new[] { "both-in" }, await Find(every));
        CollectionAssert.AreEquivalent(new[] { "both-in", "one-in" }, await Find(any));
    }

    [Test]
    public async Task Races_MatchupMirrorAndRandom()
    {
        await Insert(
            Game("hu-ud", Patch2Start.AddHours(1), race1: Race.HU, race2: Race.UD),
            Game("hu-hu", Patch2Start.AddHours(2), race1: Race.HU, race2: Race.HU),
            Game("rnd-as-hu-vs-ud", Patch2Start.AddHours(3), race1: Race.RnD, rndRace1: Race.HU, race2: Race.UD),
            Game("ne-oc", Patch2Start.AddHours(4), race1: Race.NE, race2: Race.OC));

        CollectionAssert.AreEquivalent(
            new[] { "hu-ud", "rnd-as-hu-vs-ud" },
            await Find(new ReplayExportFilter { Race = Race.UD, OpponentRace = Race.HU }));
        CollectionAssert.AreEquivalent(
            new[] { "hu-hu" },
            await Find(new ReplayExportFilter { Race = Race.HU, OpponentRace = Race.HU }));
        CollectionAssert.AreEquivalent(
            new[] { "hu-ud", "hu-hu", "rnd-as-hu-vs-ud" },
            await Find(new ReplayExportFilter { Race = Race.HU }));
    }

    [Test]
    public async Task OnlyFloGamesOfTheGameMode()
    {
        await Insert(
            Game("flo-1v1", Patch2Start.AddHours(1)),
            Game("no-flo-id", Patch2Start.AddHours(2), floMatchId: null),
            Game("2v2", Patch2Start.AddHours(3), gameMode: GameMode.GM_2v2));

        CollectionAssert.AreEquivalent(new[] { "flo-1v1" }, await Find(new ReplayExportFilter()));
    }

    [Test]
    public async Task Seasons_CombineWithThePatch()
    {
        await Insert(
            Game("s21", Patch1Start.AddDays(-30), season: 21),
            Game("s22", Patch1Start.AddDays(1), season: 22),
            Game("s23", Patch1Start.AddDays(2), season: 23),
            Game("s23-other-patch", Patch2Start.AddHours(1), season: 23));

        CollectionAssert.AreEquivalent(new[] { "s23", "s23-other-patch" }, await Find(new ReplayExportFilter { Seasons = [23] }));
        CollectionAssert.AreEquivalent(new[] { "s22", "s23", "s23-other-patch" }, await Find(new ReplayExportFilter { Seasons = [22, 23] }));
        CollectionAssert.AreEquivalent(new[] { "s23" }, await Find(new ReplayExportFilter { Seasons = [23], Patch = "3.0.0" }));
    }

    [Test]
    public async Task ExcludedGames_AreLeftOut()
    {
        var sold = Game("sold", Patch2Start.AddHours(1));
        await Insert(sold, Game("new", Patch2Start.AddHours(2)));

        CollectionAssert.AreEquivalent(new[] { "new" }, await Find(new ReplayExportFilter(), [sold.Id]));
    }

    private async Task<List<string>> Find(ReplayExportFilter filter, IReadOnlyCollection<ObjectId> excludedIds = null)
    {
        var query = ReplayExportQuery.Build(filter, _patchOptions, excludedIds);
        return (await _matches.Find(query).ToListAsync()).Select(m => m.MatchId).ToList();
    }

    private Task Insert(params Matchup[] matches) => _matches.InsertManyAsync(matches);

    private static int _nextFloId = 1000;

    private static Matchup Game(
        string name,
        DateTime endTime,
        int mmr1 = 1800,
        int mmr2 = 1800,
        Race race1 = Race.HU,
        Race? rndRace1 = null,
        Race race2 = Race.OC,
        int? floMatchId = -1,
        GameMode gameMode = GameMode.GM_1v1,
        int season = 23)
    {
        return new Matchup
        {
            Id = ObjectId.GenerateNewId(endTime),
            MatchId = name,
            FloMatchId = floMatchId == -1 ? ++_nextFloId : floMatchId,
            GameMode = gameMode,
            GateWay = GateWay.Europe,
            Season = season,
            StartTime = endTime.AddMinutes(-15),
            EndTime = endTime,
            Duration = TimeSpan.FromMinutes(15),
            Teams =
            [
                new Team { Players = [new PlayerOverviewMatches { BattleTag = "a#1", Race = race1, RndRace = rndRace1, OldMmr = mmr1, Won = true }] },
                new Team { Players = [new PlayerOverviewMatches { BattleTag = "b#2", Race = race2, OldMmr = mmr2 }] },
            ],
        };
    }
}
