using System.IO;
using FluentAssertions;
using NUnit.Framework;
using W3ChampionsStatisticService.PersonalSettings.Hotkeys;

namespace WC3ChampionsStatisticService.Tests.PersonalSettings.Hotkeys;

[TestFixture]
public class SyncedPreferencesTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "PersonalSettings", "Hotkeys", "Fixtures", name));

    [Test]
    public void Filter_RealFile_KeepsPlayerPreferences()
    {
        var filtered = SyncedPreferences.Filter(Fixture("real-war3preferences.txt"));

        filtered.Should().Contain("[Command Grid Hotkeys 00]");
        filtered.Should().Contain("[Inventory Hotkeys 0]");
        filtered.Should().Contain("[Mouse Wheel Up]");
        filtered.Should().Contain("[Reforged]");
        filtered.Should().Contain("hudscale=100");
        filtered.Should().Contain("keyscroll=50");
        filtered.Should().Contain("sfxvolume=22");
        filtered.Should().Contain("enabledObserverChat=true");
        filtered.Should().Contain("spellfilter=1");
        filtered.Should().Contain("[Custom Hotkeys 7]");
    }

    [Test]
    public void Filter_RealFile_DropsPcAndVolatileKeys()
    {
        var filtered = SyncedPreferences.Filter(Fixture("real-war3preferences.txt"));

        foreach (var forbidden in new[]
        {
            "reswidth", "refreshrate", "adapter", "outputsounddev", "netgameport", "ammrace", "schedrace",
            "allyFilter", "reducemouselag", "customkeys", "[Misc]", "[Mac]", "[Map]", "[String]", "[Commandbar Hotkeys",
        })
        {
            filtered.Should().NotContain(forbidden);
        }
    }

    [Test]
    public void Filter_RealFile_IsCanonical()
    {
        var filtered = SyncedPreferences.Filter(Fixture("real-war3preferences.txt"));

        filtered.Should().StartWith("[Command Grid Hotkeys 00]\nHeroOnly=0\nHotkey=81\n");
        filtered.Should().NotContain("\r");
        filtered.Should().EndWith("\n");
        SyncedPreferences.Filter(filtered).Should().Be(filtered);
    }

    [Test]
    public void Filter_IsOrderAndLineEndingIndependent()
    {
        var ordered = "[Sound]\nsfx=1\nambient=0\n[Input]\nkeyscroll=50\nmousescroll=40\n[Reforged]\nUnits=1\nHeroes=0\n";
        var shuffledCrlf = "[Reforged]\r\nHeroes=0\r\nUnits=1\r\n[Input]\r\nmousescroll=40\r\nkeyscroll=50\r\n[Sound]\r\nambient=0\r\nsfx=1\r\n";

        SyncedPreferences.Filter(shuffledCrlf).Should().Be(SyncedPreferences.Filter(ordered));
        SyncedPreferences.Filter(ordered).Should().Be(
            "[Input]\nkeyscroll=50\nmousescroll=40\n\n[Reforged]\nHeroes=0\nUnits=1\n\n[Sound]\nambient=0\nsfx=1\n");
    }

    [Test]
    public void Filter_Empty_ReturnsEmpty()
    {
        SyncedPreferences.Filter("").Should().BeEmpty();
        SyncedPreferences.Filter(null).Should().BeEmpty();
    }

    [Test]
    public void Filter_AllowListedKeyWithEmptyValue_IsDropped()
    {
        var filtered = SyncedPreferences.Filter("[Gameplay]\nhudscale=\ntooltips=1\n[Sound]\nsfx=\n");

        filtered.Should().Be("[Gameplay]\ntooltips=1\n");
    }

    [Test]
    public void Filter_NonEmptyInvalidValue_IsKeptSoTheValidatorRejectsIt()
    {
        var filtered = SyncedPreferences.Filter("[Gameplay]\nhudscale=abc\n");
        var result = HotkeySetupValidator.Validate(1, null, "[Gameplay]\nhudscale=abc\n", filtered);

        filtered.Should().Be("[Gameplay]\nhudscale=abc\n");
        result.Reason.Should().Be("preference_settings_invalid_value");
    }

    [Test]
    public void Filter_DuplicateSectionsAndKeys_MergeAndLastWins()
    {
        var raw = "[gameplay]\nHudScale=50\ntooltips=1\n[Input]\nkeyscroll=1\n[GAMEPLAY]\nhudscale=75\n";

        SyncedPreferences.Filter(raw).Should().Be("[GAMEPLAY]\nhudscale=75\ntooltips=1\n\n[Input]\nkeyscroll=1\n");
    }

    [Test]
    public void Filter_IgnoresLinesBeforeAnySectionAndLinesWithoutEquals()
    {
        var raw = "keyscroll=9\nstray text\n[Input]\nnot a pair\nkeyscroll=50\n";

        SyncedPreferences.Filter(raw).Should().Be("[Input]\nkeyscroll=50\n");
    }

    [TestCase("Command Grid Hotkeys 23", true)]
    [TestCase("Command Grid Hotkeys 24", false)]
    [TestCase("Command Grid Hotkeys 30", false)]
    [TestCase("Inventory Hotkeys 5", true)]
    [TestCase("Inventory Hotkeys 6", false)]
    [TestCase("Custom Hotkeys 7", true)]
    [TestCase("Custom Hotkeys 8", false)]
    [TestCase("Custom Hotkeys 10", false)]
    [TestCase("Mouse Mid Button Down", true)]
    [TestCase("Mouse Wheel Down", true)]
    [TestCase("Mouse Left Button", false)]
    [TestCase("Commandbar Hotkeys 00", false)]
    [TestCase("reforged", true)]
    public void Filter_WholeSectionPatterns(string section, bool kept)
    {
        var filtered = SyncedPreferences.Filter($"[{section}]\nHotkey=81\n");

        if (kept)
        {
            filtered.Should().Be($"[{section}]\nHotkey=81\n");
        }
        else
        {
            filtered.Should().BeEmpty();
        }
    }

    [Test]
    public void Filter_PerKeySections_KeepOnlyListedKeys()
    {
        var raw = "[Video]\nspellfilter=1\nreswidth=1920\n[Input]\nkeyscroll=5\nreducemouselag=1\ncustomkeys=1\n[Sound]\nsfx=1\noutputsounddev=2\n[Gameplay]\nHUDSCALE=100\nnetgameport=6112\nallyFilter=1\n";

        SyncedPreferences.Filter(raw).Should().Be(
            "[Gameplay]\nHUDSCALE=100\n\n[Input]\nkeyscroll=5\n\n[Sound]\nsfx=1\n\n[Video]\nspellfilter=1\n");
    }

    [Test]
    public void Filter_BomPrefixedFile_FiltersIdenticallyToPlain()
    {
        var plain = Fixture("real-war3preferences.txt");

        SyncedPreferences.Filter("\uFEFF" + plain).Should().Be(SyncedPreferences.Filter(plain));
        SyncedPreferences.Filter("\uFEFF[Input]\nkeyscroll=5\n").Should().Be("[Input]\nkeyscroll=5\n");
    }

    [Test]
    public void Filter_TolerantOfWhitespaceAroundHeadersKeysAndValues()
    {
        var raw = "[Input] \n  keyscroll = 50 \n\t[Gameplay]\t\n\thudscale\t=\t100\n";

        SyncedPreferences.Filter(raw).Should().Be("[Gameplay]\nhudscale=100\n\n[Input]\nkeyscroll=50\n");
    }
}
