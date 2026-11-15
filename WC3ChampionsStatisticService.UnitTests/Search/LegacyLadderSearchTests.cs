using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;
using static WC3ChampionsStatisticService.UnitTests.Search.SearchTestFixtures;

namespace WC3ChampionsStatisticService.UnitTests.Search;

// GET api/ladder/search — the LEGACY ladder search.
//
// These are characterisation tests. The consolidation does not change this endpoint; it stays live as
// the website's rollback target and because the in-game UI bundle
// (storage.w3champions.com/{env}/integration/w3champions.js) still calls it. The tests pin today's
// behaviour so that retiring or altering the route later is a deliberate act with a failing test
// attached, rather than a silent break of a consumer nobody was tracking.
[TestFixture]
public class LegacyLadderSearchTests
{
    private Mock<IRankRepository> _rankRepo;
    private Mock<IPlayerRepository> _playerRepo;

    [SetUp]
    public void SetUp()
    {
        _rankRepo = new Mock<IRankRepository>();
        _playerRepo = new Mock<IPlayerRepository>();

        _rankRepo
            .Setup(r => r.SearchPlayerOfLeague(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()))
            .ReturnsAsync([]);
        _playerRepo
            .Setup(p => p.SearchForPlayer(It.IsAny<string>()))
            .ReturnsAsync([]);
    }

    private LadderController CreateController()
    {
        return LadderControllerWith(_rankRepo.Object, _playerRepo.Object);
    }

    // --- Minimum length -----------------------------------------------------------------------
    //
    // This is the only search route that enforces a minimum length server-side. global-search and
    // players?search= leave it to the client.

    [TestCase("mo")]
    [TestCase("")]
    [TestCase(null)]
    public async Task MinLength_UnderThreeIsRejected(string searchFor)
    {
        var result = await CreateController().SearchPlayer(searchFor, 13);

        Assert.IsInstanceOf<BadRequestObjectResult>(result);
    }

    [Test]
    public async Task MinLength_ThreeCharactersIsAccepted()
    {
        var result = await CreateController().SearchPlayer("moo", 13);

        Assert.IsInstanceOf<OkObjectResult>(result);
    }

    // --- The hybrid response ------------------------------------------------------------------
    //
    // The response is two concatenated blocks in one flat array: ranked rows for the requested
    // {season, gateway, gameMode}, then a globally-matched tail of zeroed rows for everyone else
    // whose battleTag matches. Consumers fork on player.games > 0. This shape is exactly what the
    // consolidated flow replaces — global-search plus ranks-for-players — and pinning it makes the
    // difference explicit.

    [Test]
    public async Task Hybrid_RankedBlockAndUnrankedTailAreConcatenated()
    {
        _rankRepo
            .Setup(r => r.SearchPlayerOfLeague(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<GateWay>(), It.IsAny<GameMode>()))
            .ReturnsAsync([RankFor("Moon#1", league: 2, rankNumber: 42)]);
        _playerRepo
            .Setup(p => p.SearchForPlayer(It.IsAny<string>()))
            .ReturnsAsync([PlayerOverallStats.Create("Moonlight#2")]);

        var result = await CreateController().SearchPlayer("moon", 13);

        var ranks = (List<Rank>)((OkObjectResult)result).Value;
        Assert.AreEqual(2, ranks.Count);
        Assert.AreEqual(42, ranks[0].RankNumber, "the ranked block comes first");
        Assert.AreEqual(0, ranks[1].RankNumber, "the unranked tail is appended after it");
    }

    [Test]
    public async Task Hybrid_TailRowsAreFullyZeroed()
    {
        _playerRepo
            .Setup(p => p.SearchForPlayer(It.IsAny<string>()))
            .ReturnsAsync([PlayerOverallStats.Create("Moonlight#2")]);

        var result = await CreateController().SearchPlayer("moon", 13);

        var tail = ((List<Rank>)((OkObjectResult)result).Value).Single();
        Assert.AreEqual(0, tail.League);
        Assert.AreEqual(0, tail.RankNumber);
        Assert.AreEqual(0, tail.RankingPoints);
        Assert.AreEqual(GameMode.Undefined, tail.GameMode);
        Assert.AreEqual(GateWay.Undefined, tail.Gateway);
    }

    [Test]
    public async Task Hybrid_TailIsNotScopedToTheRequestedContext()
    {
        // The tail comes from an unfiltered directory scan: the same battleTag search runs regardless
        // of season, gateway or gameMode. This is why an exact-tag search can return a player twice —
        // once ranked, once in the tail.
        _playerRepo
            .Setup(p => p.SearchForPlayer(It.IsAny<string>()))
            .ReturnsAsync([PlayerOverallStats.Create("Moon#1")]);

        await CreateController().SearchPlayer("moon", 13, GateWay.America, GameMode.GM_4v4_AT);

        _playerRepo.Verify(
            p => p.SearchForPlayer("moon"),
            Times.Once,
            "the tail query takes only the search term — no season, gateway or gameMode");
    }

    [Test]
    public async Task Hybrid_RankedBlockIsScopedToTheRequestedContext()
    {
        await CreateController().SearchPlayer("moon", 13, GateWay.America, GameMode.GM_4v4_AT);

        _rankRepo.Verify(
            r => r.SearchPlayerOfLeague("moon", 13, GateWay.America, GameMode.GM_4v4_AT),
            Times.Once);
    }

    [Test]
    public async Task Hybrid_DefaultsAreEuropeAnd1v1()
    {
        // Callers may omit gateWay and gameMode entirely; the route still answers.
        await CreateController().SearchPlayer("moon", 13);

        _rankRepo.Verify(
            r => r.SearchPlayerOfLeague("moon", 13, GateWay.Europe, GameMode.GM_1v1),
            Times.Once);
    }
}
