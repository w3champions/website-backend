using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerProfiles.GlobalSearch;
using W3ChampionsStatisticService.Ports;
using static WC3ChampionsStatisticService.UnitTests.Search.SearchTestFixtures;

namespace WC3ChampionsStatisticService.UnitTests.Search;

// SUBSCRIBER CONTRACTS — what backend changes must not break.
//
// This file exists for one purpose: every test here is a promise made to a real, identified client
// that ships today. A failure means a backend change has broken a known subscriber, not that a
// refactor needs tidying up.
//
// It is deliberately separate from the other suites in this folder. GlobalSearchTests and
// RanksForPlayersTests verify that the search behaves correctly. These verify that it still behaves
// the way a specific subscriber already relies on. The overlap is intentional — a logic test can be
// legitimately rewritten when the logic changes, and that must not silently take a subscriber
// guarantee with it.
//
// Each test states its subscriber and the exact call that subscriber makes. Where a subscriber's
// source is known, the call site is cited so the claim can be re-checked rather than trusted.
//
// Registry of subscribers (audited 2026-07-18):
//   1. Website          — this repo's frontend, both the flag-on and flag-off paths.
//   2. launcher-e       — the Tauri launcher. Active, shipped.
//   3. In-game UI       — storage.w3champions.com/{env}/integration/w3champions.js, loaded by the
//                         WC3 glue UI that the deprecated Electron launcher installs. Source repo
//                         UNIDENTIFIED, so it can never be migrated on demand — treat as frozen.
//
// LIMITS OF THIS FILE — read before trusting it:
//   - These run at the controller boundary, so routing, model binding and JSON serialisation are NOT
//     exercised. A change to serialiser configuration (casing, null handling, enum-as-string) could
//     break a subscriber with every test here still green.
//   - Adding a subscriber means adding tests here by hand. Nothing discovers them.
//   - A subscriber that changes what it needs will not be reflected here until someone updates it.
//     That direction is intentionally unprotected: this file guards subscribers against the backend,
//     not the backend against subscribers.
[TestFixture]
public class SubscriberContractTests
{
    private Mock<IRankRepository> _rankRepo;
    private Mock<IPlayerRepository> _playerRepo;

    [SetUp]
    public void SetUp()
    {
        _rankRepo = new Mock<IRankRepository>();
        _playerRepo = new Mock<IPlayerRepository>();
    }

    private static PlayersController PlayersControllerOver(List<W3ChampionsStatisticService.PersonalSettings.PersonalSetting> directory)
    {
        return PlayersControllerWith(playerService: PlayerServiceWith(directory));
    }

    private LadderController LadderController()
    {
        return LadderControllerWith(_rankRepo.Object, _playerRepo.Object);
    }

    private void EnrichmentReturns(params Rank[] ranks)
    {
        _rankRepo
            .Setup(r => r.LoadRanksForPlayers(It.IsAny<List<string>>(), It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()))
            .ReturnsAsync([.. ranks]);
    }

    private static List<RankInContext> RanksOf(IActionResult result)
    {
        return (List<RankInContext>)((OkObjectResult)result).Value;
    }

    private static List<PlayerSearchInfo> SearchResult(IActionResult result)
    {
        Assert.IsInstanceOf<OkObjectResult>(result, "subscribers treat any non-200 as a failure");
        return (List<PlayerSearchInfo>)((OkObjectResult)result).Value;
    }

    // =========================================================================================
    // SUBSCRIBER: launcher-e  (Tauri launcher — active, shipped)
    // Call site: src/services/player-search.service.ts:48
    // Store:     src/models/player-search.ts:40-63
    // =========================================================================================

    [Test]
    public async Task LauncherE_GlobalSearch_ReturnsABareArray()
    {
        // player-search.service.ts:56 casts the body directly to IPlayerSearchResult[]. Wrapping the
        // response in a paging envelope — however reasonable an API improvement — breaks it outright.
        var result = await PlayersControllerOver(Settings("Moon#1")).GlobalSearchPlayer("moon");

        Assert.IsInstanceOf<List<PlayerSearchInfo>>(((OkObjectResult)result).Value);
    }

    [Test]
    public async Task LauncherE_GlobalSearch_HonoursPageSizeTenExactly()
    {
        // player-search.ts:63 sets hasMore = (received == PAGE_SIZE), with PAGE_SIZE = 10. If the
        // backend substitutes its own page size, that comparison is false on a full page and the
        // launcher's infinite scroll stops after page one — silently, with no error anywhere.
        var directory = Settings(Enumerable.Range(1, 40).Select(i => $"Moon{i:00}#{i}").ToArray());

        var result = await PlayersControllerOver(directory).GlobalSearchPlayer("moon", pageSize: 10);

        Assert.AreEqual(10, SearchResult(result).Count);
    }

    [Test]
    public async Task LauncherE_GlobalSearch_ItemsCarryEveryFieldItDereferences()
    {
        // PlayerSearch.tsx:150,154,162 and player-search.ts:43. None of these are null-guarded on the
        // launcher side, so a null here is a TypeError in a shipped desktop client.
        var directory = Settings("Moon#1", "Moonlight#2");

        var result = await PlayersControllerOver(directory).GlobalSearchPlayer("moon");

        foreach (var item in SearchResult(result))
        {
            Assert.IsNotNull(item.BattleTag, "PlayerSearch.tsx:150 calls .split('#') on it");
            Assert.IsNotEmpty(item.RelevanceId, "player-search.ts:43 uses it as the paging cursor");
            Assert.IsNotNull(item.ProfilePicture, "PlayerSearch.tsx:154 reads .race/.pictureId/.isClassic");
            Assert.IsNotNull(item.Seasons, "PlayerSearch.tsx:162 maps over it");
        }
    }

    [Test]
    public async Task LauncherE_GlobalSearch_CursorPagingAdvancesWithoutRepeating()
    {
        // The launcher appends pages rather than replacing them (player-search.ts:25), so a repeated
        // entry becomes a visible duplicate row rather than a silent no-op.
        var directory = Settings(Enumerable.Range(1, 40).Select(i => $"Moon{i:00}#{i}").ToArray());
        var controller = PlayersControllerOver(directory);

        var page1 = SearchResult(await controller.GlobalSearchPlayer("moon", pageSize: 10));
        var page2 = SearchResult(await controller.GlobalSearchPlayer(
            "moon", lastRelevanceId: page1.Last().RelevanceId, pageSize: 10));

        CollectionAssert.IsEmpty(
            page1.Select(p => p.BattleTag).Intersect(page2.Select(p => p.BattleTag)));
    }

    [Test]
    public async Task LauncherE_GlobalSearch_EmptyResultIsAnEmptyArrayNotAnError()
    {
        // A non-200 is mapped to null and then to "clear the list" (player-search.ts:64-69), which
        // renders identically to a failed request. No-matches must stay a 200.
        var result = await PlayersControllerOver(Settings("Grubby#1")).GlobalSearchPlayer("moon");

        Assert.IsEmpty(SearchResult(result));
    }

    // =========================================================================================
    // SUBSCRIBER: In-game UI bundle  (FROZEN — source repo unidentified)
    // storage.w3champions.com/{env}/integration/w3champions.js
    // Call site: StatisticsClient.prototype.searchRankings
    // =========================================================================================

    [Test]
    public async Task InGameUi_LadderSearch_AcceptsItsExactCallShape()
    {
        // The bundle builds: api/ladder/search?gateWay={gw}&searchFor={name}&gameMode={gm}&season={s}
        // and passes EGateway.Europe with the user's current mode filter and selected season.
        _rankRepo
            .Setup(r => r.SearchPlayerOfLeague(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()))
            .ReturnsAsync([]);
        _playerRepo.Setup(p => p.SearchForPlayer(It.IsAny<string>())).ReturnsAsync([]);

        var result = await LadderController().SearchPlayer("moon", 13, GateWay.Europe, GameMode.GM_1v1);

        Assert.IsInstanceOf<OkObjectResult>(result);
    }

    [Test]
    public async Task InGameUi_LadderSearch_StillReturnsTheHybridArrayItForksOn()
    {
        // The bundle distinguishes ranked from unranked by player.games > 0 on a single flat array.
        // Splitting this into two collections, or dropping the zeroed tail, breaks a client that
        // cannot be patched on our schedule.
        _rankRepo
            .Setup(r => r.SearchPlayerOfLeague(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()))
            .ReturnsAsync([RankFor("Moon#1", league: 2, rankNumber: 42)]);
        _playerRepo
            .Setup(p => p.SearchForPlayer(It.IsAny<string>()))
            .ReturnsAsync([PlayerOverallStats.Create("Moonlight#2")]);

        var result = await LadderController().SearchPlayer("moon", 13);

        var rows = (List<Rank>)((OkObjectResult)result).Value;
        Assert.AreEqual(2, rows.Count, "one flat array holding both blocks");
        Assert.IsNotNull(rows[0].Player, "every row carries a player object the client dereferences");
        Assert.IsNotNull(rows[1].Player);
    }

    [Test]
    public async Task InGameUi_LadderSearch_MinimumLengthIsStillThree()
    {
        // The bundle guards on searchPhrase.length >= 3 before calling. Raising the server-side
        // minimum would reject calls it considers valid.
        _rankRepo
            .Setup(r => r.SearchPlayerOfLeague(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()))
            .ReturnsAsync([]);
        _playerRepo.Setup(p => p.SearchForPlayer(It.IsAny<string>())).ReturnsAsync([]);

        Assert.IsInstanceOf<OkObjectResult>(await LadderController().SearchPlayer("moo", 13));
    }

    [Test]
    public void InGameUi_LadderSearch_MemberIdsStaysOffTheWire()
    {
        // MemberIds exists for the database lookup only. Rank rows serialize straight out of
        // api/ladder/search, so dropping the JsonIgnore would change the wire shape of every
        // rank-returning legacy endpoint.
        var json = System.Text.Json.JsonSerializer.Serialize(TeamRankFor(["Moon#1", "Mate#2"]));

        Assert.IsFalse(json.ToLowerInvariant().Contains("memberids"), "Rank.MemberIds leaked into the response JSON");
    }

    // =========================================================================================
    // SUBSCRIBER: Website  (this repo's frontend)
    // =========================================================================================

    [Test]
    public async Task Website_GlobalSearch_HonoursPageSizeTwentyExactly()
    {
        // Both the header store (globalSearch/store.ts:24) and the ladder search use
        // hasMore = (received == PAGE_SIZE) with PAGE_SIZE = 20 — the same heuristic launcher-e uses.
        var directory = Settings(Enumerable.Range(1, 60).Select(i => $"Moon{i:00}#{i}").ToArray());

        var result = await PlayersControllerOver(directory).GlobalSearchPlayer("moon", pageSize: 20);

        Assert.AreEqual(20, SearchResult(result).Count);
    }

    [Test]
    public async Task Website_RanksForPlayers_CarriesTheFieldsScrollToRowNeeds()
    {
        // ranking/store.ts:54,59 read league and rankNumber. league selects which ladder page to load,
        // rankNumber is the DOM anchor (#listitem_{n}). Losing either breaks click-to-ladder-row
        // without breaking anything visible in the search dropdown itself.
        EnrichmentReturns(RankFor("Moon#1", league: 3, rankNumber: 501, rankingPoints: 420));

        var result = await LadderController().GetRanksForPlayers(Request(["Moon#1"]));

        var rank = RanksOf(result).Single();
        Assert.AreEqual(3, rank.League);
        Assert.AreEqual(501, rank.RankNumber);
        Assert.AreEqual(420, rank.RankingPoints);
    }

    [Test]
    public async Task Website_RanksForPlayers_CarriesPlayerIdentityAndGateway()
    {
        // ranking/store.ts:45 builds a row key as `${battleTag}@${gateWay}` joined across players, and
        // :64 reads players[0].name for the display row.
        EnrichmentReturns(RankFor("Moon#1", gateWay: GateWay.America));

        var result = await LadderController().GetRanksForPlayers(Request(["Moon#1"], gateWay: GateWay.America));

        var rank = RanksOf(result).Single();
        Assert.AreEqual(GateWay.America, rank.GateWay);
        Assert.IsNotEmpty(rank.Players, "the row key is built from players[]");
        Assert.AreEqual("Moon#1", rank.Players[0].BattleTag);
        Assert.IsNotNull(rank.Players[0].Name, "store.ts:64 reads players[0].name");
    }

    [Test]
    public async Task Website_RanksForPlayers_CarriesRaceSoPerRaceRowsStaySeparate()
    {
        // 1v1 is ranked per race, so one battleTag can hold several ranks. store.ts:24-34 keys them by
        // tag and relies on race to keep the rows distinct rather than collapsing them.
        EnrichmentReturns(
            RankFor("Moon#1", league: 1, rankNumber: 500, race: Race.HU),
            RankFor("Moon#1", league: 3, rankNumber: 501, race: Race.UD));

        var result = await LadderController().GetRanksForPlayers(Request(["Moon#1"]));

        var ranks = RanksOf(result);
        Assert.AreEqual(2, ranks.Count);
        CollectionAssert.AreEquivalent(new Race?[] { Race.HU, Race.UD }, ranks.Select(r => r.Race).ToList());
    }

    [Test]
    public async Task Website_LegacyLadderSearchStillServes_ItIsTheRollbackPath()
    {
        // USE_NEW_SEARCH=false routes the ladder back to api/ladder/search. It must keep working for
        // the flag to be a real rollback lever rather than a decoration.
        _rankRepo
            .Setup(r => r.SearchPlayerOfLeague(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()))
            .ReturnsAsync([]);
        _playerRepo.Setup(p => p.SearchForPlayer(It.IsAny<string>())).ReturnsAsync([]);

        Assert.IsInstanceOf<OkObjectResult>(await LadderController().SearchPlayer("moon", 13));
    }

    [Test]
    public async Task Website_LegacyPlayerSearchStillServes_ItIsThePickersRollbackPath()
    {
        // The other half of the same lever: with the flag off, PlayerSearch.vue — the shared picker
        // behind ~12 admin/clan/profile surfaces — goes back to api/players/?search=.
        _playerRepo
            .Setup(p => p.SearchForPlayer(It.IsAny<string>()))
            .ReturnsAsync([PlayerOverallStats.Create("Moon#1")]);

        var controller = new PlayersController(_playerRepo.Object, null, null, null, null, null, null, null);

        var result = await controller.SearchPlayer("moon");

        var players = (List<PlayerOverallStats>)((OkObjectResult)result).Value;
        Assert.AreEqual("Moon#1", players[0].BattleTag, "the picker consumes battleTag off these rows");
    }
}
