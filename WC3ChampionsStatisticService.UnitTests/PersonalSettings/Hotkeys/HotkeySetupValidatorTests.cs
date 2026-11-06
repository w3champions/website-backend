using System.IO;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using W3ChampionsStatisticService.PersonalSettings.Hotkeys;

namespace WC3ChampionsStatisticService.Tests.PersonalSettings.Hotkeys;

[TestFixture]
public class HotkeySetupValidatorTests
{
    private const string ValidPreferences = "[Gameplay]\nhudscale=100\nenabledObserverChat=true\n";

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "PersonalSettings", "Hotkeys", "Fixtures", name));

    private static HotkeyValidationResult ValidateCustomKeys(string customKeys, int mode = 1) =>
        HotkeySetupValidator.Validate(mode, customKeys, ValidPreferences, SyncedPreferences.Filter(ValidPreferences));

    [TestCase("real-reforged-customkeys.txt")]
    [TestCase("launcher-generated-customkeys.txt")]
    [TestCase("jcfieldsdev-grid-en-customkeys.txt")]
    [TestCase("jcfieldsdev-qweaz-en-customkeys.txt")]
    public void CustomKeysFixtures_AreValid(string fixture)
    {
        var result = ValidateCustomKeys(Fixture(fixture));

        result.Should().Be(HotkeyValidationResult.Valid);
        result.Reason.Should().BeNull();
    }

    [Test]
    public void RealCustomKeys_WithCrlf_IsValid()
    {
        var crlf = Fixture("real-reforged-customkeys.txt").Replace("\r\n", "\n").Replace("\n", "\r\n");

        ValidateCustomKeys(crlf).IsValid.Should().BeTrue();
    }

    [Test]
    public void RealCustomKeys_WithLeadingBom_IsValid()
    {
        ValidateCustomKeys("\uFEFF" + Fixture("real-reforged-customkeys.txt")).IsValid.Should().BeTrue();
    }

    [Test]
    public void CustomKeys_BomOnlyAtStart_IsTolerated()
    {
        ValidateCustomKeys("\uFEFF[hpea]\nHotkey=Q\n").IsValid.Should().BeTrue();
        ValidateCustomKeys("[hpea]\n\uFEFFHotkey=Q\n").Reason.Should().Be("custom_keys_invalid_line");
    }

    [TestCase("[Orc] ")]
    [TestCase("\t[Orc]\t")]
    [TestCase("  // comment")]
    [TestCase("\t// comment \t")]
    [TestCase("   ")]
    [TestCase(" Hotkey=Q")]
    [TestCase("\tHotkey=Q")]
    [TestCase("Hotkey =Q")]
    [TestCase("  Hotkey \t= Q ")]
    public void CustomKeys_NotepadWhitespace_IsTolerated(string line)
    {
        ValidateCustomKeys("[hpea]\n" + line + "\n").IsValid.Should().BeTrue();
    }

    [TestCase("[Or c] ")]
    [TestCase(" Payload=x")]
    [TestCase(" = x")]
    [TestCase("hello world ")]
    public void CustomKeys_WhitespaceDoesNotRescueInvalidLines(string line)
    {
        ValidateCustomKeys("[hpea]\n" + line + "\n").Reason.Should().Be("custom_keys_invalid_line");
    }

    [Test]
    public void RealPreferences_FilteredAndRaw_AreValid()
    {
        var raw = Fixture("real-war3preferences.txt");

        HotkeySetupValidator.Validate(2, null, raw, SyncedPreferences.Filter(raw)).IsValid.Should().BeTrue();
    }

    [TestCase(3)]
    [TestCase(-1)]
    public void InvalidMode_IsRejected(int mode)
    {
        ValidateCustomKeys("", mode).Reason.Should().Be("invalid_mode");
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void ValidModes_AreAccepted(int mode)
    {
        ValidateCustomKeys(null, mode).IsValid.Should().BeTrue();
    }

    [Test]
    public void CustomKeys_AtLimit_IsValid_AndOneByteOverIsTooLarge()
    {
        var atLimit = BuildCustomKeysOfBytes(524_288);
        var overLimit = BuildCustomKeysOfBytes(524_289);

        Encoding.UTF8.GetByteCount(atLimit).Should().Be(524_288);
        ValidateCustomKeys(atLimit).IsValid.Should().BeTrue();
        ValidateCustomKeys(overLimit).Reason.Should().Be("custom_keys_too_large");
    }

    [Test]
    public void CustomKeys_SizeIsMeasuredInUtf8Bytes()
    {
        // 262,145 two-byte characters in a comment line are 524,290 bytes but only 262,147 chars.
        var multiByte = "//" + new string('é', 262_144);

        ValidateCustomKeys(multiByte).Reason.Should().Be("custom_keys_too_large");
    }

    [TestCase("hello world")]
    [TestCase("[Name With Spaces]")]
    [TestCase("[]")]
    [TestCase("Payload=x")]
    [TestCase("=x")]
    [TestCase("Hotkey=\u0001")]
    [TestCase("Hotkey=a\u007fb")]
    public void CustomKeys_InvalidLine_IsRejected(string line)
    {
        ValidateCustomKeys("[hpea]\n" + line + "\n").Reason.Should().Be("custom_keys_invalid_line");
    }

    [Test]
    public void CustomKeys_ValueOf513Chars_IsRejected_And512IsAccepted()
    {
        ValidateCustomKeys("[hpea]\nHotkey=" + new string('a', 512)).IsValid.Should().BeTrue();
        ValidateCustomKeys("[hpea]\nHotkey=" + new string('a', 513)).Reason.Should().Be("custom_keys_invalid_line");
    }

    [Test]
    public void CustomKeys_TabInValue_IsAccepted_AndKeyMatchingIsCaseInsensitive()
    {
        ValidateCustomKeys("[hpea]\nHOTKEY=a\tb\nbuttonpos=1,2\n// note\n\n").IsValid.Should().BeTrue();
    }

    [TestCase("EditorSuffix=x")]
    [TestCase("Position=1")]
    [TestCase("AwakenTip=x")]
    [TestCase("ReviveTip=x")]
    public void CustomKeys_KeysFoundInSharedPacks_AreAccepted(string line)
    {
        ValidateCustomKeys("[hpea]\n" + line + "\n").IsValid.Should().BeTrue();
    }

    [Test]
    public void RawPreferences_TooLarge_IsRejectedFirst()
    {
        var tooBig = "[Gameplay]\n" + new string('x', 32_769);

        var result = HotkeySetupValidator.Validate(9, "garbage", tooBig, "");

        result.Reason.Should().Be("preferences_too_large");
        HotkeySetupValidator.Validate(1, null, new string('x', 32_768), "").IsValid.Should().BeTrue();
    }

    [Test]
    public void PreferenceSettings_InvalidValue_IsRejected()
    {
        HotkeySetupValidator.Validate(1, null, "", "[Gameplay]\nhudscale=abc\n").Reason
            .Should().Be("preference_settings_invalid_value");
    }

    [TestCase("hudscale=1234567")]
    [TestCase("hudscale=")]
    [TestCase("hudscale=TRUE1")]
    [TestCase("hudscale=1.5")]
    public void PreferenceSettings_OtherInvalidShapes_AreRejected(string line)
    {
        HotkeySetupValidator.Validate(1, null, "", "[Gameplay]\n" + line + "\n").IsValid.Should().BeFalse();
    }

    [TestCase("hudscale=-120")]
    [TestCase("hudscale=999999")]
    [TestCase("tooltips=true")]
    [TestCase("tooltips=false")]
    public void PreferenceSettings_ValidShapes_AreAccepted(string line)
    {
        HotkeySetupValidator.Validate(1, null, "", "[Gameplay]\n" + line + "\n").IsValid.Should().BeTrue();
    }

    [Test]
    public void NullCustomKeys_WithValidPreferences_IsValid()
    {
        var result = HotkeySetupValidator.Validate(0, null, ValidPreferences, SyncedPreferences.Filter(ValidPreferences));

        result.Should().Be(HotkeyValidationResult.Valid);
    }

    private static string BuildCustomKeysOfBytes(int bytes)
    {
        const string header = "[hpea]\n//";
        return header + new string('a', bytes - header.Length);
    }
}
