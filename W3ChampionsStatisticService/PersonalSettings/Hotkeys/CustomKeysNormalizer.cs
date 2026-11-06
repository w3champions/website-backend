using System;
using System.Linq;

namespace W3ChampionsStatisticService.PersonalSettings.Hotkeys;

/// <summary>
/// Normalises CustomKeys text the same way the launcher does before comparing:
/// CRLF/CR become LF, trailing whitespace is stripped per line, trailing blank lines are dropped,
/// and null is equivalent to empty.
/// </summary>
public static class CustomKeysNormalizer
{
    public static string Normalize(string customKeys)
    {
        if (string.IsNullOrEmpty(customKeys))
        {
            return string.Empty;
        }

        var lines = customKeys.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToList();

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join("\n", lines);
    }

    public static bool AreEquivalent(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
}
