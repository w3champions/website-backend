using NUnit.Framework;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

[TestFixture]
public class MapFilePathsTests
{
    [Test]
    public void GamePathBecomesTheUpdateServiceFilePath()
    {
        Assert.That(
            MapFilePaths.ToUpdateServiceFilePath(@"maps\W3Champions\(3)NomadIsles_fix.w3x"),
            Is.EqualTo("W3Champions/(3)NomadIsles_fix.w3x"));
    }

    [TestCase(@"Maps\W3Champions\x.w3x", "W3Champions/x.w3x")]
    [TestCase("maps/W3Champions/x.w3x", "W3Champions/x.w3x")]
    [TestCase(@"maps\W3Champions\Temporary\x.w3x", "W3Champions/Temporary/x.w3x")]
    [TestCase(@"maps\maps\x.w3x", "maps/x.w3x")]
    [TestCase(@"W3Champions\x.w3x", "W3Champions/x.w3x")]
    public void DropsExactlyOneLeadingMapsSegmentAndKeepsCase(string gamePath, string expected)
    {
        Assert.That(MapFilePaths.ToUpdateServiceFilePath(gamePath), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(@"maps\")]
    public void EmptyPathsHaveNoFilePath(string gamePath)
    {
        Assert.That(MapFilePaths.ToUpdateServiceFilePath(gamePath), Is.Null);
    }
}
