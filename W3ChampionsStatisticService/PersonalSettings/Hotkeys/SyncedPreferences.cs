using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace W3ChampionsStatisticService.PersonalSettings.Hotkeys;

/// <summary>
/// The only copy of the synced War3Preferences.txt allow-list. Reduces the raw file the launcher
/// uploads to canonical text: allow-listed sections and keys only, sections and keys sorted
/// case-insensitively, LF line endings. Everything else is dropped.
/// </summary>
public static class SyncedPreferences
{
    private static readonly Regex WholeSectionPattern = new(
        @"^(Command Grid Hotkeys [0-2][0-3]|Inventory Hotkeys [0-5]|Custom Hotkeys [0-7]|Mouse (Mid Button Down|Wheel Up|Wheel Down)|Reforged)\z",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // ASCII only: no Unicode case folding (e.g. the Kelvin sign U+212A must never match 'k').
    private static readonly Regex KeyPattern = new(
        @"^[A-Za-z0-9_]{1,64}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, HashSet<string>> KeysPerSection = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Input"] = Keys("keyscroll", "mousescroll", "mousescrolldisable", "confinemousecursor"),
        ["Gameplay"] = Keys(
            "defaultZoom", "minZoom", "minimumZoom", "maxZoom", "enabledEnhancedZoom", "commandbuttonhotkey",
            "peonDoubleTapFocus", "subgrouporder", "formationtoggle", "formations",
            "healthbars", "coloredhealthbars", "herobar", "heroframes", "herolevel", "hudscale", "hudsidepanels",
            "numericCooldown", "tooltips", "showtimeelapsed", "displayapm", "displayfps", "displayping",
            "creepFilter", "terrainFilter", "denyIcon", "goldmineUnitCounter", "showAirToGroundIndicators",
            "classicCursor", "cinePortraits", "occlusion", "multiboardon",
            "enabledGameInfoMessages", "enabledGlobalChat", "enabledObserverChat", "enabledOpponentChat",
            "enabledTeamChat", "enabledAdvancedObserverUi", "gamespeed", "autosaveReplay", "useSkins",
            "profanity", "teen", "bgMenuMovie", "sdCampaignScreens"),
        ["Sound"] = Keys(
            "ambient", "assetmode", "classicsound", "environmental", "movement", "music", "musicvolume",
            "positional", "sfx", "sfxvolume", "subtitles", "unit"),
        ["Video"] = Keys("spellfilter"),
    };

    public static string Filter(string rawPreferences)
    {
        if (string.IsNullOrEmpty(rawPreferences))
        {
            return string.Empty;
        }

        var sections = new Dictionary<string, Section>(StringComparer.OrdinalIgnoreCase);
        Section current = null;

        // A leading UTF-8 BOM (written by some editors) is not part of the first header.
        var text = rawPreferences[0] == '\uFEFF' ? rawPreferences.Substring(1) : rawPreferences;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length >= 2 && line[0] == '[' && line[^1] == ']')
            {
                current = ResolveSection(sections, line.Substring(1, line.Length - 2).Trim());
                continue;
            }

            var separator = line.IndexOf('=');
            if (current == null || separator <= 0)
            {
                continue;
            }

            var key = line.Substring(0, separator).Trim();
            var value = line.Substring(separator + 1).Trim();
            if (!KeyPattern.IsMatch(key) || !current.Accepts(key))
            {
                continue;
            }

            // The last occurrence wins; an empty last value drops the key.
            if (value.Length == 0)
            {
                current.Values.Remove(key);
            }
            else
            {
                current.Values[key] = (key, value);
            }
        }

        return Render(sections.Values);
    }

    private static Section ResolveSection(Dictionary<string, Section> sections, string name)
    {
        if (!IsAscii(name))
        {
            return null;
        }

        var isWhole = WholeSectionPattern.IsMatch(name);
        if (!isWhole && !KeysPerSection.ContainsKey(name))
        {
            return null;
        }

        var allowedKeys = isWhole ? null : KeysPerSection[name];
        if (!sections.TryGetValue(name, out var section))
        {
            section = new Section(allowedKeys);
            sections[name] = section;
        }

        // The output keeps the casing of the last occurrence.
        section.Name = name;
        return section;
    }

    private static string Render(IEnumerable<Section> sections)
    {
        var builder = new StringBuilder();
        foreach (var section in sections.Where(s => s.Values.Count > 0).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append('[').Append(section.Name).Append("]\n");
            foreach (var (key, value) in section.Values.Values.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append(key).Append('=').Append(value).Append('\n');
            }
        }

        return builder.ToString();
    }

    private static bool IsAscii(string text) => text.All(c => c < 128);

    private static HashSet<string> Keys(params string[] keys) => new(keys, StringComparer.OrdinalIgnoreCase);

    private sealed class Section(HashSet<string> allowedKeys)
    {
        public string Name { get; set; }

        public Dictionary<string, (string Key, string Value)> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool Accepts(string key) => allowedKeys == null || allowedKeys.Contains(key);
    }
}
