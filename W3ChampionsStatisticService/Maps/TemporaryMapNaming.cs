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

    /// <summary>The root update-service's upload form does not take: its fileName is the fileKey below this.</summary>
    private const string UpdateServiceRoot = "W3Champions/";

    /// <summary>The root matchmaking and flo put every map path under, with backslashes.</summary>
    private const string GameMapRoot = @"maps\";

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

    /// <summary>
    /// The strict fileKey shape, for a path matchmaking hands back before anything is written at it (S-I2, S2-1): what
    /// <see cref="BuildFileKey"/> emits and nothing wider — <see cref="TemporaryMapKeys.IsFilePath"/>, exactly one
    /// segment under the prefix, shaped <c>&lt;stem&gt;-&lt;8 lowercase hex&gt;.w3x|.w3m</c> with a non-empty stem, the
    /// extension in either case, and no control character (U+0000-U+001F, U+007F-U+009F) anywhere in the name. Every
    /// <see cref="BuildFileKey"/> output satisfies it.
    /// </summary>
    public static bool IsFileKey(string path)
    {
        if (!TemporaryMapKeys.IsFilePath(path))
        {
            return false;
        }

        var name = path[TemporaryMapKeys.PathPrefix.Length..];
        if (name.Contains('/') || ContainsControlCharacter(name) || !TryGetExtension(name, out var extension))
        {
            return false;
        }

        var stemAndSuffix = name[..^extension.Length];
        var dash = stemAndSuffix.Length - (Sha1SuffixLength + 1);
        return dash > 0
               && stemAndSuffix[dash] == '-'
               && TemporaryMapKeys.IsLowercaseHex(stemAndSuffix[(dash + 1)..], Sha1SuffixLength);
    }

    /// <summary>true when <paramref name="value"/> holds a C0 or C1 control character (U+0000-U+001F, U+007F-U+009F).</summary>
    public static bool ContainsControlCharacter(string value)
    {
        foreach (var c in value ?? string.Empty)
        {
            if (IsControl(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// update-service's upload form takes the fileKey without its "W3Champions/" root. Anything else is refused rather
    /// than sliced: a slice of a foreign path would name a file outside the temporary folder.
    /// </summary>
    public static string UpdateServiceFileName(string fileKey)
        => fileKey != null && fileKey.StartsWith(UpdateServiceRoot, StringComparison.Ordinal)
            ? fileKey[UpdateServiceRoot.Length..]
            : throw new ArgumentException("The fileKey must start with the update-service root.", nameof(fileKey));

    /// <summary>matchmaking's and flo's gameMap.path: exactly the fileKey with backslashes under a "maps\" root.</summary>
    public static string GameMapPath(string fileKey)
        => TemporaryMapKeys.IsFilePath(fileKey)
            ? GameMapRoot + fileKey.Replace('/', '\\')
            : throw new ArgumentException("The fileKey must be a temporary map file path.", nameof(fileKey));

    /// <summary>
    /// The name forwarded to matchmaking as originalFileName: the uploader's value with the C0 and C1 control
    /// characters (U+0000-U+001F, U+007F-U+009F) removed and nothing else changed (S-L5). Null reads as empty.
    /// </summary>
    public static string RemoveControlCharacters(string value)
    {
        value ??= string.Empty;
        var kept = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!IsControl(c))
            {
                kept.Append(c);
            }
        }

        return kept.Length == value.Length ? value : kept.ToString();
    }

    private static bool IsControl(char c) => c < ' ' || (c >= '\u007f' && c <= '\u009f');

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
            if (!IsControl(c) && Array.IndexOf(ReservedCharacters, c) < 0)
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
