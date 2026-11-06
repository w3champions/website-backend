using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace W3ChampionsStatisticService.PersonalSettings.Hotkeys;

public record HotkeyValidationResult(bool IsValid, string Reason)
{
    public static HotkeyValidationResult Valid { get; } = new(true, null);

    public static HotkeyValidationResult Invalid(string reason) => new(false, reason);
}

/// <summary>
/// Guards hotkey setup uploads against arbitrary or abusive content. Pure, never logs content.
/// </summary>
public static class HotkeySetupValidator
{
    public const int MaxCustomKeysBytes = 524_288;
    public const int MaxRawPreferencesBytes = 32_768;
    public const int MaxCustomKeysValueLength = 512;

    private static readonly Regex SectionHeader = new(
        @"^\[[A-Za-z0-9_]{1,40}\]\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PreferenceValue = new(
        @"^(-?[0-9]{1,6}|true|false)\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> AllowedCustomKeysKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hotkey", "Unhotkey", "Researchhotkey", "Buttonpos", "Unbuttonpos", "Researchbuttonpos",
        "MerchantAvailable", "GameCommand", "Modifier", "MenuCommand", "CameraCommand", "CtrlGroupCommand",
        "ObserverCommand", "ReplayCommand", "OffensiveStructure", "CanAttackGround", "CanPrioritizeAir",
        "IgnoreBuilds", "Attackless", "UnhotkeyId", "ParentId", "Tip", "Untip", "Ubertip", "Unubertip",
        "Researchtip", "Researchubertip", "Hoktey", "EditorSuffix", "Position", "AwakenTip", "ReviveTip",
    };

    public static HotkeyValidationResult Validate(int mode, string customKeys, string rawPreferences, string preferenceSettings)
    {
        if (ByteCount(rawPreferences) > MaxRawPreferencesBytes)
        {
            return HotkeyValidationResult.Invalid("preferences_too_large");
        }

        if (mode < 0 || mode > 2)
        {
            return HotkeyValidationResult.Invalid("invalid_mode");
        }

        if (customKeys != null)
        {
            if (ByteCount(customKeys) > MaxCustomKeysBytes)
            {
                return HotkeyValidationResult.Invalid("custom_keys_too_large");
            }

            if (!AreCustomKeysLinesValid(customKeys))
            {
                return HotkeyValidationResult.Invalid("custom_keys_invalid_line");
            }
        }

        return ArePreferenceValuesValid(preferenceSettings)
            ? HotkeyValidationResult.Valid
            : HotkeyValidationResult.Invalid("preference_settings_invalid_value");
    }

    private static int ByteCount(string text) => text == null ? 0 : Encoding.UTF8.GetByteCount(text);

    private static bool AreCustomKeysLinesValid(string customKeys)
    {
        foreach (var rawLine in customKeys.Split('\n'))
        {
            var line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            if (!IsCustomKeysLineValid(line))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCustomKeysLineValid(string line)
    {
        if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
        {
            return !HasForbiddenControlCharacter(line);
        }

        if (SectionHeader.IsMatch(line))
        {
            return true;
        }

        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            return false;
        }

        var key = line.Substring(0, separator);
        var value = line.Substring(separator + 1);
        return AllowedCustomKeysKeys.Contains(key)
            && value.Length <= MaxCustomKeysValueLength
            && !HasForbiddenControlCharacter(value);
    }

    private static bool HasForbiddenControlCharacter(string text)
    {
        foreach (var c in text)
        {
            if (char.IsControl(c) && c != '\t')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ArePreferenceValuesValid(string preferenceSettings)
    {
        if (string.IsNullOrEmpty(preferenceSettings))
        {
            return true;
        }

        foreach (var rawLine in preferenceSettings.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '[')
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0 || !PreferenceValue.IsMatch(line.Substring(separator + 1)))
            {
                return false;
            }
        }

        return true;
    }
}
