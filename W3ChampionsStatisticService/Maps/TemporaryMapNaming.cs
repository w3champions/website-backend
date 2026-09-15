using System;
using System.Buffers;
using System.Text;
using W3C.Domain.Maps;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// The normative file-name rules of design spec §6.4. One canonical value comes out of here — the
/// <b>fileKey</b>, <c>W3Champions/CustomGames/&lt;name&gt;-&lt;sha1_8&gt;&lt;ext&gt;</c> — and every
/// other spelling in the system is derived from it (update-service's stored FilePath, the upload
/// form's fileName minus the W3Champions/ prefix, matchmaking's backslashed gameMap.path, and the
/// launcher's mapPath query parameter). website-backend is the only producer: no other service
/// re-implements these rules.
/// <para>
/// Deliberately readable: letters, digits, spaces and all other punctuation survive, including
/// non-Latin scripts and format characters such as U+FEFF (§6.4 does not remove them). Uniqueness
/// comes from the sha1 suffix, never from mangling the name. Path traversal is impossible because
/// both separators are removed, leading dots are trimmed, and this class prepends the prefix and
/// appends the "-&lt;sha1_8&gt;&lt;ext&gt;" suffix, so the name is always inside one segment that is
/// never "." or "..".
/// </para>
/// </summary>
public static class TemporaryMapNaming
{
    private const string FallbackName = "map";
    private const int Sha1SuffixLength = 8;

    private static readonly char[] ReservedCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>"Dots and spaces": U+002E and U+0020 only, stripped in any interleaving.</summary>
    private static readonly char[] TrimmedEdges = ['.', ' '];

    /// <summary>true for ".w3x"/".w3m" (ordinal, case-insensitive); the out value is always lowercase.</summary>
    public static bool TryGetExtension(string originalFileName, out string extension)
    {
        extension = null;
        var name = originalFileName ?? string.Empty;

        if (name.EndsWith(".w3x", StringComparison.OrdinalIgnoreCase))
        {
            extension = ".w3x";
        }
        else if (name.EndsWith(".w3m", StringComparison.OrdinalIgnoreCase))
        {
            extension = ".w3m";
        }

        return extension != null;
    }

    /// <summary>
    /// The §6.4 pipeline, in the spec's order: strip the extension → NFC normalise → remove control
    /// characters (&lt; U+0020, U+007F-U+009F) and the reserved set → trim leading/trailing dots and
    /// spaces → collapse runs of whitespace to one space → truncate to 100 characters, then trim
    /// again → empty ⇒ "map".
    /// <para>
    /// Two inputs §6.4 does not define are handled without changing any other output: code units
    /// that <see cref="string.Normalize(NormalizationForm)"/> rejects (unpaired surrogates, U+FFFE)
    /// are dropped before normalising, and the truncation never splits a surrogate pair, so the
    /// result is always well-formed UTF-16 of at most 100 UTF-16 code units.
    /// </para>
    /// </summary>
    public static string Sanitise(string originalFileName)
    {
        var name = originalFileName ?? string.Empty;
        if (TryGetExtension(name, out var extension))
        {
            name = name[..^extension.Length];
        }

        name = DropCodeUnitsThatCannotBeNormalised(name).Normalize(NormalizationForm.FormC);
        name = RemoveControlAndReservedCharacters(name);
        name = name.Trim(TrimmedEdges);
        name = CollapseWhitespaceRuns(name);
        name = Truncate(name, TemporaryMapLimits.MaxSanitisedNameLength).Trim(TrimmedEdges);

        return name.Length == 0 ? FallbackName : name;
    }

    /// <summary>
    /// The canonical fileKey. <paramref name="extension"/> must come from
    /// <see cref="TryGetExtension"/> and <paramref name="sha1"/> must be the 40-hex sha1 of the
    /// stored bytes (either case); anything else throws, so a caller mistake cannot put a separator
    /// or a traversal into the path through the suffix.
    /// </summary>
    public static string BuildFileKey(string originalFileName, string sha1, string extension)
    {
        if (extension is not (".w3x" or ".w3m"))
        {
            throw new ArgumentException("The extension must be \".w3x\" or \".w3m\" from TryGetExtension.", nameof(extension));
        }

        var lowercaseSha1 = sha1?.ToLowerInvariant();
        if (!MapProof.IsLowercaseHex(lowercaseSha1, TemporaryMapKeys.Sha1HexLength))
        {
            throw new ArgumentException("The sha1 must be 40 hexadecimal characters.", nameof(sha1));
        }

        return $"{TemporaryMapLimits.TempMapPathPrefix}{Sanitise(originalFileName)}-{lowercaseSha1[..Sha1SuffixLength]}{extension}";
    }

    // Unpaired surrogates (reachable through a JSON "\ud800" escape) and U+FFFE make Normalize throw.
    private static string DropCodeUnitsThatCannotBeNormalised(string value)
    {
        var kept = new StringBuilder(value.Length);
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
            if (status == OperationStatus.Done && rune.Value != 0xFFFE)
            {
                kept.Append(remaining[..consumed]);
            }

            remaining = remaining[consumed..];
        }

        return kept.ToString();
    }

    private static string RemoveControlAndReservedCharacters(string value)
    {
        var kept = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            var isControl = c < ' ' || (c >= '\u007f' && c <= '\u009f');
            if (!isControl && Array.IndexOf(ReservedCharacters, c) < 0)
            {
                kept.Append(c);
            }
        }

        return kept.ToString();
    }

    /// <summary>
    /// "Whitespace" is Unicode White_Space, which is exactly <see cref="char.IsWhiteSpace(char)"/>
    /// (and .NET's regex \s) — not JavaScript's \s, which adds U+FEFF and drops U+0085. After control
    /// removal, the 19 remaining members are U+0020, U+00A0, U+1680, U+2000-U+200A, U+2028, U+2029,
    /// U+202F, U+205F and U+3000.
    /// </summary>
    private static string CollapseWhitespaceRuns(string value)
    {
        var collapsed = new StringBuilder(value.Length);
        var previousWasWhitespace = false;
        foreach (var c in value)
        {
            var isWhitespace = char.IsWhiteSpace(c);
            if (!isWhitespace)
            {
                collapsed.Append(c);
            }
            else if (!previousWasWhitespace)
            {
                collapsed.Append(' ');
            }

            previousWasWhitespace = isWhitespace;
        }

        return collapsed.ToString();
    }

    // The input is well-formed here, so a high surrogate in the last kept position always has its
    // low surrogate just past the cap; cutting one earlier keeps the pair out whole.
    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var length = char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength;
        return value[..length];
    }
}
