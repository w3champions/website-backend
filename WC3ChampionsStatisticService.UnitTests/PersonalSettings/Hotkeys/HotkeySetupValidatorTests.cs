using System;
using System.IO;
using System.Linq;
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
        // 262,146 characters (a 2-char marker plus 262,144 two-byte characters) are 524,290 UTF-8 bytes.
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
    public void RawPreferences_AtLimitIsValid_AndOneByteOverIsRejectedFirst()
    {
        HotkeySetupValidator.Validate(1, null, new string('x', 32_768), "").IsValid.Should().BeTrue();

        var result = HotkeySetupValidator.Validate(9, "garbage", new string('x', 32_769), new string('y', 20_000));

        result.Reason.Should().Be("preferences_too_large");
    }

    [Test]
    public void PreferenceSettings_AtLimitIsValid_AndOneByteOverIsRejected()
    {
        HotkeySetupValidator.Validate(1, null, "", BuildSettingsOfBytes(16_384)).IsValid.Should().BeTrue();
        HotkeySetupValidator.Validate(1, null, "", BuildSettingsOfBytes(16_385)).Reason
            .Should().Be("preference_settings_too_large");
    }

    [Test]
    public void PreferenceSettings_TooLarge_IsCheckedBeforeValues()
    {
        var oversizedAndInvalid = BuildSettingsOfBytes(16_385) + "hudscale=abc\n";

        HotkeySetupValidator.Validate(1, null, "", oversizedAndInvalid).Reason.Should().Be("preference_settings_too_large");
    }

    [TestCase("[Reforged]\n[x=hello world\0payload")]
    [TestCase("[Reforged]\nanything \u0001 goes here=1")]
    [TestCase("[Reforged]\nanything goes here=1")]
    [TestCase("[Reforged]\ncaf\u00e9=1")]
    [TestCase("[Reforged\nHeroes=0")]
    [TestCase("[Re forged\u0001]\nHeroes=0")]
    [TestCase("[]\nHeroes=0")]
    [TestCase("=1")]
    [TestCase("no separator")]
    public void PreferenceSettings_MalformedHeadersAndKeys_AreRejected(string settings)
    {
        HotkeySetupValidator.Validate(1, null, "", settings).Reason.Should().Be("preference_settings_invalid_value");
    }

    [Test]
    public void PreferenceSettings_16000CharKey_IsRejected()
    {
        HotkeySetupValidator.Validate(1, null, "", "[Reforged]\n" + new string('k', 16_000) + "=1\n").Reason
            .Should().Be("preference_settings_invalid_value");
    }

    [Test]
    public void PreferenceSettings_FilterOutputOfRealFile_StillPasses()
    {
        var settings = SyncedPreferences.Filter(Fixture("real-war3preferences.txt"));

        HotkeySetupValidator.Validate(1, null, "", settings + "\r\n").IsValid.Should().BeTrue();
    }

    [TestCase("\u2028")]
    [TestCase("\u2029")]
    public void CustomKeys_UnicodeLineSeparatorsInValue_AreRejected(string separator)
    {
        ValidateCustomKeys("[hpea]\nTip=a" + separator + "b\n").Reason.Should().Be("custom_keys_invalid_line");
        ValidateCustomKeys("[hpea]\n// a" + separator + "b\n").Reason.Should().Be("custom_keys_invalid_line");
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

    private static string BuildSettingsOfBytes(int bytes)
    {
        const string header = "[Reforged]\n";
        var remaining = bytes - header.Length;
        for (var keyLength = 1; keyLength <= 64; keyLength++)
        {
            var rest = remaining - (keyLength + "=1\n".Length);
            if (rest >= 0 && rest % "k=1\n".Length == 0)
            {
                return header + string.Concat(Enumerable.Repeat("k=1\n", rest / 4)) + new string('z', keyLength) + "=1\n";
            }
        }

        throw new InvalidOperationException("cannot build settings of " + bytes + " bytes");
    }
}
