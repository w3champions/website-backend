using FluentAssertions;
using NUnit.Framework;
using W3ChampionsStatisticService.PersonalSettings.Hotkeys;

namespace WC3ChampionsStatisticService.Tests.PersonalSettings.Hotkeys;

[TestFixture]
public class CustomKeysNormalizerTests
{
    [TestCase(null, "")]
    [TestCase("", "")]
    [TestCase("[a]\r\nHotkey=Q\r\n", "[a]\nHotkey=Q")]
    [TestCase("[a]\rHotkey=Q\r", "[a]\nHotkey=Q")]
    [TestCase("[a]  \nHotkey=Q\t\n\n\n", "[a]\nHotkey=Q")]
    [TestCase("[a]\n\nHotkey=Q", "[a]\n\nHotkey=Q")]
    public void Normalize_AppliesLauncherRules(string input, string expected)
    {
        CustomKeysNormalizer.Normalize(input).Should().Be(expected);
    }

    [Test]
    public void AreEquivalent_NullAndEmpty_AreEqual()
    {
        CustomKeysNormalizer.AreEquivalent(null, "").Should().BeTrue();
    }

    [Test]
    public void AreEquivalent_DifferentContent_AreNotEqual()
    {
        CustomKeysNormalizer.AreEquivalent("a", "b").Should().BeFalse();
    }
}
