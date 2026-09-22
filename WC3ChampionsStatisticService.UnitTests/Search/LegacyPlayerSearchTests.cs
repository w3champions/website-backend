using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;
using static WC3ChampionsStatisticService.UnitTests.Search.SearchTestFixtures;

namespace WC3ChampionsStatisticService.UnitTests.Search;

// GET api/players/?search= — the LEGACY directory search.
//
// The oldest of the three search modes and the one global-search replaces for the player picker. Like
// LegacyLadderSearchTests these are characterisation tests: the consolidation does not change this
// route, it keeps it serving as the picker's rollback target when USE_NEW_SEARCH is off.
//
// Its distinguishing property is what it does NOT do — no pagination, no cap, no relevance ordering.
// That is pinned here deliberately: those absences are the reason the endpoint is being retired, and
// a future change that quietly adds a cap would alter rollback behaviour rather than fix it.
//
// Unlike api/ladder/search, this route has no known consumer outside this repo (audited 2026-07-18),
// so it is the one legacy endpoint retirable on the website's own schedule.
[TestFixture]
public class LegacyPlayerSearchTests
{
    private Mock<IPlayerRepository> _playerRepo;

    [SetUp]
    public void SetUp()
    {
        _playerRepo = new Mock<IPlayerRepository>();
        _playerRepo
            .Setup(p => p.SearchForPlayer(It.IsAny<string>()))
            .ReturnsAsync([]);
    }

    private PlayersController CreateController()
    {
        return PlayersControllerWith(_playerRepo.Object);
    }

    // --- Request validation -------------------------------------------------------------------
    //
    // The only guard this route has. Note it is weaker than api/ladder/search: no minimum length, so
    // a single character is accepted and scans the whole collection.

    [TestCase("")]
    [TestCase(null)]
    public async Task Validation_EmptyOrNullIsRejected(string search)
    {
        var result = await CreateController().SearchPlayer(search);

        Assert.IsInstanceOf<BadRequestObjectResult>(result);
    }

    [Test]
    public async Task Validation_ASingleCharacterIsAccepted()
    {
        // Characterisation, not endorsement: there is no server-side minimum here, which is why a
        // one-character search can return tens of thousands of rows.
        var result = await CreateController().SearchPlayer("m");

        Assert.IsInstanceOf<OkObjectResult>(result);
    }

    [Test]
    public async Task Validation_RejectedRequestsDoNotHitTheRepository()
    {
        await CreateController().SearchPlayer("");

        _playerRepo.Verify(p => p.SearchForPlayer(It.IsAny<string>()), Times.Never);
    }

    // --- Behaviour ----------------------------------------------------------------------------

    [Test]
    public async Task Search_PassesTheTermThroughUnmodified()
    {
        await CreateController().SearchPlayer("Moon#123");

        _playerRepo.Verify(p => p.SearchForPlayer("Moon#123"), Times.Once);
    }

    [Test]
    public async Task Search_ReturnsThePlayerOverallStatsShape()
    {
        // The picker's legacy path consumes only battleTag off these rows, but the whole heavy
        // document is what goes over the wire — the reason the swap to global-search is worth making.
        _playerRepo
            .Setup(p => p.SearchForPlayer(It.IsAny<string>()))
            .ReturnsAsync([PlayerOverallStats.Create("Moon#1")]);

        var result = await CreateController().SearchPlayer("moon");

        var players = (List<PlayerOverallStats>)((OkObjectResult)result).Value;
        Assert.AreEqual(1, players.Count);
        Assert.AreEqual("Moon#1", players[0].BattleTag);
    }

    [Test]
    public async Task Search_AppliesNoCapOrPagination()
    {
        // Every match is returned in one response. There is no pageSize parameter to pass and none is
        // applied server-side — pinned because adding one silently would change rollback behaviour.
        var many = new List<PlayerOverallStats>();
        for (var i = 0; i < 500; i++)
        {
            many.Add(PlayerOverallStats.Create($"Moon{i}#{i}"));
        }
        _playerRepo.Setup(p => p.SearchForPlayer(It.IsAny<string>())).ReturnsAsync(many);

        var result = await CreateController().SearchPlayer("moon");

        Assert.AreEqual(500, ((List<PlayerOverallStats>)((OkObjectResult)result).Value).Count);
    }

    [Test]
    public async Task Search_NoMatchesReturnsAnEmptyListNotAnError()
    {
        var result = await CreateController().SearchPlayer("nobody");

        Assert.IsEmpty((List<PlayerOverallStats>)((OkObjectResult)result).Value);
    }
}
