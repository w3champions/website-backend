using NUnit.Framework;
using W3ChampionsStatisticService.Admin.ReplayExport;

namespace WC3ChampionsStatisticService.Tests.Admin.ReplayExport;

[TestFixture]
public class ReplayExportTextTests
{
    [TestCase(null, null)]
    [TestCase("", null)]
    [TestCase("  \t\n ", null)]
    [TestCase("  UD mirrors  ", "UD mirrors")]
    [TestCase("line one\nline two", "line one line two")]
    [TestCase("a\u0007b\r\nc", "a b  c")]
    public void CleanText_GivesOneLineWithoutControlCharacters(string text, string expected)
    {
        Assert.AreEqual(expected, ReplayExportControllerBase.CleanText(text, 100));
    }

    [Test]
    public void CleanText_CutsLongTextButNotAnEmoji()
    {
        Assert.AreEqual("abcde", ReplayExportControllerBase.CleanText("abcdefgh", 5));
        // "abcd" + 😀 (two chars): a cut after 5 chars would keep half of the emoji.
        Assert.AreEqual("abcd", ReplayExportControllerBase.CleanText("abcd😀x", 5));
        Assert.AreEqual("abc😀", ReplayExportControllerBase.CleanText("abc😀x", 5));
    }
}
