using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerProfiles.GlobalSearch;
using static WC3ChampionsStatisticService.UnitTests.Search.SearchTestFixtures;

namespace WC3ChampionsStatisticService.UnitTests.Search;

// GET api/players/global-search at the controller boundary.
//
// GlobalSearchTests covers the search itself; this covers what only the controller does — clamping
// pageSize. The cap is the guard that stops an anonymous caller from pulling the directory in bulk.
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
}
