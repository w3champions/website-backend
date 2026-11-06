using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Ports;
using static WC3ChampionsStatisticService.UnitTests.Search.SearchTestFixtures;

namespace WC3ChampionsStatisticService.UnitTests.Search;

// POST api/ladder/ranks-for-players — rank-in-context enrichment.
//
// The second half of the consolidated flow: global-search returns a page of directory hits, this
// endpoint annotates that page with ladder rank for one {season, gateway, gameMode}. Players absent
// from the response are unranked in that context — absence is the signal, there is no "unranked" row.
[TestFixture]
public class RanksForPlayersTests
{
    private Mock<IRankRepository> _rankRepo;
    private Mock<IPlayerRepository> _playerRepo;

    [SetUp]
    public void SetUp()
    {
        _rankRepo = new Mock<IRankRepository>();
        _playerRepo = new Mock<IPlayerRepository>();
    }

    private LadderController CreateController()
    {
        return LadderControllerWith(_rankRepo.Object, _playerRepo.Object);
    }

    private void EnrichmentReturns(params Rank[] ranks)
    {
        _rankRepo
            .Setup(r => r.LoadRanksForPlayers(
                It.IsAny<List<string>>(), It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()))
            .ReturnsAsync([.. ranks]);
    }

    private static List<RankInContext> RanksOf(IActionResult result)
    {
        return (List<RankInContext>)((OkObjectResult)result).Value;
    }

    // --- Request validation -------------------------------------------------------------------
    //
    // The route is anonymous, so the bound on batch size is what keeps the $in query small.

    [Test]
    public async Task Validation_NullRequestReturnsEmptyList_NotAnError()
    {
        // A caller with nothing to enrich should get an empty enrichment, not a 400 to handle.
        var result = await CreateController().GetRanksForPlayers(null);

        Assert.IsEmpty(RanksOf(result));
    }

    [Test]
    public async Task Validation_NullBattleTagsReturnsEmptyList()
    {
        var result = await CreateController().GetRanksForPlayers(Request(null));

        Assert.IsEmpty(RanksOf(result));
    }

    [Test]
    public async Task Validation_EmptyBattleTagsReturnsEmptyList()
    {
        var result = await CreateController().GetRanksForPlayers(Request([]));

        Assert.IsEmpty(RanksOf(result));
    }

    [Test]
    public async Task Validation_EmptyRequestDoesNotHitTheRepository()
    {
        await CreateController().GetRanksForPlayers(Request([]));

        _rankRepo.Verify(
            r => r.LoadRanksForPlayers(
                It.IsAny<List<string>>(), It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()),
            Times.Never);
    }

    [Test]
    public async Task Validation_AtTheBatchLimitIsAccepted()
    {
        var tags = Enumerable.Range(1, RanksForPlayersRequest.MaxBattleTags)
            .Select(i => $"Player{i}#{i}")
            .ToList();
        EnrichmentReturns();

        var result = await CreateController().GetRanksForPlayers(Request(tags));

        Assert.IsInstanceOf<OkObjectResult>(result);
    }

    [Test]
    public async Task Validation_OverTheBatchLimitIsRejected()
    {
        var tags = Enumerable.Range(1, RanksForPlayersRequest.MaxBattleTags + 1)
            .Select(i => $"Player{i}#{i}")
            .ToList();

        var result = await CreateController().GetRanksForPlayers(Request(tags));

        Assert.IsInstanceOf<BadRequestObjectResult>(result);
    }

    // --- Context scoping ----------------------------------------------------------------------
    //
    // The whole point of the endpoint: a rank is only meaningful inside one season/gateway/gameMode.
    // The pre-existing 2-argument overload filters by season alone and must not be the one called.

    [Test]
    public async Task Context_AllFourScopingArgumentsReachTheRepository()
    {
        EnrichmentReturns();

        await CreateController().GetRanksForPlayers(
            Request(["Moon#1"], season: 13, gateWay: GateWay.America, gameMode: GameMode.GM_2v2_AT));

        _rankRepo.Verify(
            r => r.LoadRanksForPlayers(
                It.Is<List<string>>(t => t.Count == 1 && t[0] == "Moon#1"),
                13,
                GateWay.America,
                GameMode.GM_2v2_AT),
            Times.Once);
    }

    [Test]
    public async Task Context_TheSeasonOnlyOverloadIsNotUsed()
    {
        EnrichmentReturns();

        await CreateController().GetRanksForPlayers(Request(["Moon#1"]));

        _rankRepo.Verify(
            r => r.LoadRanksForPlayers(It.IsAny<List<string>>(), It.IsAny<int>()),
            Times.Never);
    }

    // --- Response mapping ---------------------------------------------------------------------

    [Test]
    public async Task Mapping_RankBecomesRankInContext()
    {
        EnrichmentReturns(RankFor("Moon#1", league: 2, rankNumber: 42, rankingPoints: 1234));

        var result = await CreateController().GetRanksForPlayers(Request(["Moon#1"]));

        var ranks = RanksOf(result);
        Assert.AreEqual(1, ranks.Count);
        Assert.AreEqual("Moon#1", ranks[0].Players.Single().BattleTag);
        Assert.AreEqual(2, ranks[0].League);
        Assert.AreEqual(42, ranks[0].RankNumber);
        Assert.AreEqual(1234, ranks[0].RankingPoints);
    }

    [Test]
    public async Task Mapping_LeagueAndRankNumberSurvive_TheyDriveScrollToRow()
    {
        // The website scrolls to a ladder row using league (to load the right list) and rankNumber
        // (the DOM anchor). Dropping either silently breaks that navigation.
        EnrichmentReturns(RankFor("Moon#1", league: 3, rankNumber: 501));

        var result = await CreateController().GetRanksForPlayers(Request(["Moon#1"]));

        var rank = RanksOf(result).Single();
        Assert.AreEqual(3, rank.League);
        Assert.AreEqual(501, rank.RankNumber);
    }

    [Test]
    public async Task Mapping_RaceIsCarried_ForPerRace1v1Ladders()
    {
        EnrichmentReturns(
            RankFor("Moon#1", league: 1, rankNumber: 500, race: Race.HU),
            RankFor("Moon#1", league: 3, rankNumber: 501, race: Race.UD));

        var result = await CreateController().GetRanksForPlayers(Request(["Moon#1"]));

        var ranks = RanksOf(result);
        CollectionAssert.AreEquivalent(
            new Race?[] { Race.HU, Race.UD },
            ranks.Select(r => r.Race).ToList());
    }

    [Test]
    public async Task Mapping_UnrankedPlayersAreAbsent_RatherThanZeroed()
    {
        // Two tags requested, one ranked. The other must simply not appear — the caller synthesises
        // its own "unranked" row. A zeroed row here is what produced the old duplicate-result bug.
        EnrichmentReturns(RankFor("Moon#1"));

        var result = await CreateController().GetRanksForPlayers(Request(["Moon#1", "Grubby#2"]));

        var ranks = RanksOf(result);
        Assert.AreEqual(1, ranks.Count);
        Assert.AreEqual("Moon#1", ranks[0].Players.Single().BattleTag);
    }

    [Test]
    public async Task Mapping_NoMatchesReturnsAnEmptyList()
    {
        EnrichmentReturns();

        var result = await CreateController().GetRanksForPlayers(Request(["Nobody#1"]));

        Assert.IsEmpty(RanksOf(result));
    }
}
