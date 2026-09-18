using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerProfiles.GlobalSearch;
using static WC3ChampionsStatisticService.UnitTests.Search.SearchTestFixtures;

namespace WC3ChampionsStatisticService.UnitTests.Search;

// GET api/players/global-search at the controller boundary.
//
// GlobalSearchTests covers the search itself; this covers what only the controller does — clamping
// pageSize and rejecting short searches. Together they guard the anonymous route: the cap stops
// pulling the directory in bulk, the minimum length stops near-empty terms from matching most of it
// (with a ladder context, every match feeds the standings $in query).
[TestFixture]
public class GlobalSearchControllerTests
{
    private const int PageSizeCap = 20;

    private PlayersController CreateController(int directorySize)
    {
        var directory = Settings(
            Enumerable.Range(1, directorySize).Select(i => $"Moon{i:000}#{i}").ToArray());

        return PlayersControllerWith(playerService: PlayerServiceWith(directory));
    }

    private static List<PlayerSearchInfo> ResultOf(IActionResult result)
    {
        return (List<PlayerSearchInfo>)((OkObjectResult)result).Value;
    }

    [Test]
    public async Task PageSize_DefaultsToTheCap()
    {
        var result = await CreateController(50).GlobalSearchPlayer("moon");

        Assert.AreEqual(PageSizeCap, ResultOf(result).Count);
    }

    [Test]
    public async Task PageSize_SmallerValuesAreHonoured()
    {
        // launcher-e asks for 10 and infers "there is more" from getting exactly 10 back.
        var result = await CreateController(50).GlobalSearchPlayer("moon", pageSize: 10);

        Assert.AreEqual(10, ResultOf(result).Count);
    }

    [Test]
    public async Task PageSize_LargerValuesAreClampedToTheCap()
    {
        var result = await CreateController(500).GlobalSearchPlayer("moon", pageSize: 200);

        Assert.AreEqual(PageSizeCap, ResultOf(result).Count);
    }

    [Test]
    public async Task PageSize_ExactlyTheCapIsAllowed()
    {
        var result = await CreateController(50).GlobalSearchPlayer("moon", pageSize: PageSizeCap);

        Assert.AreEqual(PageSizeCap, ResultOf(result).Count);
    }

    [Test]
    public async Task Search_ReturnsFewerThanThePageSizeWhenTheDirectoryIsSmaller()
    {
        var result = await CreateController(3).GlobalSearchPlayer("moon");

        Assert.AreEqual(3, ResultOf(result).Count);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("m")]
    [TestCase("mo")]
    public async Task MinLength_SearchesUnderThreeLettersAreRejected(string search)
    {
        var result = await CreateController(50).GlobalSearchPlayer(search);

        Assert.IsInstanceOf<BadRequestObjectResult>(result);
    }

    [Test]
    public async Task MinLength_ThreeLettersAreServed()
    {
        // Every known client gates its input at exactly three characters (website surfaces and
        // launcher-e alike), so three letters must stay served — the guard must never creep higher.
        var result = await CreateController(50).GlobalSearchPlayer("moo");

        Assert.AreEqual(PageSizeCap, ResultOf(result).Count);
    }

    // Zero-weight characters are found inside every string by culture-sensitive matching, so a term
    // made of them measures three long and matches the whole directory. Counting letters and digits
    // is what keeps the guard's promise.
    [TestCase("​​​", Description = "zero-width spaces")]
    [TestCase("­­­", Description = "soft hyphens")]
    [TestCase("﻿﻿﻿", Description = "byte-order marks")]
    [TestCase("m​​o", Description = "two letters padded to four characters")]
    public async Task MinLength_TermsWithoutThreeSearchableCharactersAreRejected(string search)
    {
        var result = await CreateController(50).GlobalSearchPlayer(search);

        Assert.IsInstanceOf<BadRequestObjectResult>(result);
    }

    [Test]
    public async Task MinLength_NonLatinLettersCount()
    {
        // BattleTags carry Cyrillic, CJK and accented names; those letters are searchable characters.
        var result = await CreateController(50).GlobalSearchPlayer("Гоб");

        Assert.IsInstanceOf<OkObjectResult>(result);
    }

    [Test]
    public async Task MinLength_DigitsCount()
    {
        var result = await CreateController(50).GlobalSearchPlayer("123");

        Assert.IsInstanceOf<OkObjectResult>(result);
    }

    // The three ladder-context parameters travel together. A partial context used to be dropped in
    // silence, which also switched the relevanceId's layout — so a caller paging across the change
    // compared cursors from two different key namespaces and got an empty page with no error.
    [TestCase(13, GateWay.Europe, null)]
    [TestCase(13, null, GameMode.GM_1v1)]
    [TestCase(null, GateWay.Europe, GameMode.GM_1v1)]
    [TestCase(13, null, null)]
    [TestCase(null, null, GameMode.GM_1v1)]
    public async Task Context_PartialLadderContextIsRejected(int? season, GateWay? gateWay, GameMode? gameMode)
    {
        var result = await CreateController(50).GlobalSearchPlayer("moo", "", 20, season, gateWay, gameMode);

        Assert.IsInstanceOf<BadRequestObjectResult>(result);
    }

    [Test]
    public async Task Context_TheFullLadderContextIsServed()
    {
        var result = await CreateController(50).GlobalSearchPlayer("moo", "", 20, 13, GateWay.Europe, GameMode.GM_1v1);

        Assert.IsInstanceOf<OkObjectResult>(result);
    }

    [Test]
    public async Task Context_OmittingItEntirelyIsServed()
    {
        // The header, the player picker and launcher-e all search without a ladder; launcher-e never
        // sends these parameters at all, so no context must stay as valid as a full one.
        var result = await CreateController(50).GlobalSearchPlayer("moo");

        Assert.IsInstanceOf<OkObjectResult>(result);
    }
}
