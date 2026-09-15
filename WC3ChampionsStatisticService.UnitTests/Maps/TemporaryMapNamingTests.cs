using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// Pins the normative sanitisation of design spec §6.4. The output is a user-visible file name AND
/// a path segment served over HTTP, so readable Unicode is kept and only separators, control
/// characters and the reserved set are removed.
/// </summary>
[TestFixture]
public class TemporaryMapNamingTests
{
    private const string Sha1 = "94ec3bda2d9edd74bac37ec48edf4eb2b05138df";
    private const string Prefix = TemporaryMapLimits.TempMapPathPrefix;
    private const string Suffix = "-94ec3bda.w3x";

    [Test]
    public void SpecExample_ProducesTheSpecFileKey()
    {
        Assert.That(
            TemporaryMapNaming.BuildFileKey("Legion TD 11.4c-hf4 (X3) Team OZE.w3x", Sha1, ".w3x"),
            Is.EqualTo("W3Champions/CustomGames/Legion TD 11.4c-hf4 (X3) Team OZE-94ec3bda.w3x"));
    }

    [TestCase("Simple Map.w3x", "Simple Map")]
    [TestCase("Simple Map.W3X", "Simple Map")]
    [TestCase("Simple Map.w3m", "Simple Map")]
    [TestCase("My   Map.w3x", "My Map")]
    [TestCase("  Spaced  .w3x", "Spaced")]
    [TestCase("...dotted....w3x", "dotted")]
    [TestCase("no extension left", "no extension left")]
    public void Sanitise_NormalisesWhitespaceAndTrimsDotsAndSpaces(string input, string expected)
    {
        Assert.That(TemporaryMapNaming.Sanitise(input), Is.EqualTo(expected));
    }

    [TestCase("../../etc/passwd.w3x", "etcpasswd")]
    [TestCase("..\\..\\windows\\system32.w3x", "windowssystem32")]
    [TestCase("con:my*map?.w3x", "conmymap")]
    [TestCase("a<b>c\"d|e.w3x", "abcde")]
    public void Sanitise_StripsPathSeparatorsAndTheReservedSet(string input, string expected)
    {
        Assert.That(TemporaryMapNaming.Sanitise(input), Is.EqualTo(expected));
    }

    [Test]
    public void Sanitise_StripsControlCharacters()
    {
        Assert.That(TemporaryMapNaming.Sanitise("a\u0000b\u0007c\u007fd\u009fe.w3x"), Is.EqualTo("abcde"));
    }

    [Test]
    public void Sanitise_RemovesExactlyTheSpecControlRanges()
    {
        // Every bound of both §6.4 ranges beside its kept neighbour: U+001F (last removed) and U+0020
        // (first kept) around "< U+0020"; U+007E (last kept) and U+007F (first removed), then U+009F
        // (last removed) and U+00A0 (first kept, then collapsed to a space) around U+007F-U+009F.
        Assert.That(TemporaryMapNaming.Sanitise("a\u001fb\u0020c~\u007fd\u009f\u00a0e.w3x"), Is.EqualTo("ab c~d e"));
    }

    [Test]
    public void Sanitise_RemovesControlCharactersBeforeCollapsingWhitespace()
    {
        // Tab, line feed and NEL are control characters, removed before whitespace is collapsed, so
        // they join the words instead of turning into a space.
        Assert.That(TemporaryMapNaming.Sanitise("Map\tOne\nTwo\u0085Three.w3x"), Is.EqualTo("MapOneTwoThree"));
    }

    [Test]
    public void Sanitise_KeepsNonLatinScriptsAndPunctuation()
    {
        Assert.That(TemporaryMapNaming.Sanitise("Тестовая карта №1 (v2).w3x"), Is.EqualTo("Тестовая карта №1 (v2)"));
        Assert.That(TemporaryMapNaming.Sanitise("東京マップ.w3x"), Is.EqualTo("東京マップ"));
    }

    [Test]
    public void Sanitise_NormalisesToNfc()
    {
        // "cafe" + COMBINING ACUTE ACCENT must collapse to the precomposed U+00E9.
        Assert.That(TemporaryMapNaming.Sanitise("cafe\u0301.w3x"), Is.EqualTo("caf\u00e9"));
    }

    [Test]
    public void Sanitise_NormalisesBeforeRemoving()
    {
        // §6.4 order: NFC runs first, while U+0000 still separates the base from its combining mark,
        // so removing U+0000 afterwards leaves the pair decomposed.
        Assert.That(TemporaryMapNaming.Sanitise("e\u0000\u0301.w3x"), Is.EqualTo("e\u0301"));
    }

    [Test]
    public void Sanitise_CollapsesEveryUnicodeWhitespaceRunToOneSpace()
    {
        // The whitespace class is Unicode White_Space (char.IsWhiteSpace), not just U+0020.
        Assert.That(TemporaryMapNaming.Sanitise("a\u00a0b.w3x"), Is.EqualTo("a b"));
        Assert.That(TemporaryMapNaming.Sanitise("a\u3000\u2003 b.w3x"), Is.EqualTo("a b"));
        Assert.That(TemporaryMapNaming.Sanitise("a\u2028\u2029b.w3x"), Is.EqualTo("a b"));
        Assert.That(TemporaryMapNaming.Sanitise("a\u1680\u202f\u205fb.w3x"), Is.EqualTo("a b"));
    }

    [Test]
    public void Sanitise_KeepsFormatCharactersBecauseTheSpecDoesNotRemoveThem()
    {
        // U+200B, U+FEFF and U+200D are not White_Space and §6.4 does not remove format (Cf)
        // characters (ruling on watch item 8: stay spec-literal).
        Assert.That(TemporaryMapNaming.Sanitise("a\u200bb\ufeffc\u200dd.w3x"), Is.EqualTo("a\u200bb\ufeffc\u200dd"));
    }

    [TestCase(". .a. .w3x", "a")]
    [TestCase(" . x . .w3x", "x")]
    public void Sanitise_TrimsInterleavedDotsAndSpacesFromBothEnds(string input, string expected)
    {
        Assert.That(TemporaryMapNaming.Sanitise(input), Is.EqualTo(expected));
    }

    [Test]
    public void Sanitise_TrimsAgainAfterCollapsing_EvenWhenNothingIsTruncated()
    {
        // NBSP and U+3000 are not "dots and spaces", so the first trim keeps them; collapsing turns
        // them into spaces at the edges, and the trim after the truncation step removes those.
        Assert.That(TemporaryMapNaming.Sanitise("\u00a0.Map.\u3000.w3x"), Is.EqualTo("Map"));
    }

    [Test]
    public void Sanitise_TrimsBeforeCollapsing_SoALeadingNonAsciiSpaceCountsTowardsTheCap()
    {
        // §6.4 order: trim dots and spaces → collapse whitespace → truncate → trim again. The leading
        // NBSP survives the first trim, becomes one of the 100 characters, and is trimmed only after
        // the truncation, which leaves 99 characters.
        var input = "\u00a0" + new string('a', 100) + ".w3x";

        Assert.That(TemporaryMapNaming.Sanitise(input), Is.EqualTo(new string('a', 99)));
    }

    [Test]
    public void Sanitise_TrimsBeforeTruncating_SoLeadingDotsAndSpacesDoNotUseTheCap()
    {
        var input = "..  .." + new string('a', 100) + ".w3x";

        Assert.That(TemporaryMapNaming.Sanitise(input), Is.EqualTo(new string('a', 100)));
    }

    [Test]
    public void Sanitise_CapsTheBaseNameAt100Characters_AndReTrims()
    {
        var long150 = new string('a', 99) + "   " + new string('b', 48);

        var result = TemporaryMapNaming.Sanitise(long150 + ".w3x");

        Assert.That(result.Length, Is.EqualTo(99), "truncation at 100 lands on the space, which is trimmed again");
        Assert.That(result, Is.EqualTo(new string('a', 99)));
    }

    [TestCase(100, 100)]
    [TestCase(101, 100)]
    public void Sanitise_CapsAtExactly100Characters(int inputLength, int expectedLength)
    {
        Assert.That(TemporaryMapNaming.Sanitise(new string('a', inputLength) + ".w3x"), Is.EqualTo(new string('a', expectedLength)));
    }

    [Test]
    public void Sanitise_TrimsATrailingDotLeftByTheTruncation()
    {
        var input = new string('a', 99) + "." + new string('b', 10) + ".w3x";

        Assert.That(TemporaryMapNaming.Sanitise(input), Is.EqualTo(new string('a', 99)));
    }

    [Test]
    public void Sanitise_NeverSplitsASurrogatePairAtTheCap()
    {
        // The emoji occupies UTF-16 indices 99 and 100; cutting at 100 would leave a lone high surrogate.
        var input = new string('a', 99) + "\U0001F600" + "b.w3x";

        Assert.That(TemporaryMapNaming.Sanitise(input), Is.EqualTo(new string('a', 99)));
    }

    [Test]
    public void Sanitise_KeepsASurrogatePairThatEndsExactlyAtTheCap()
    {
        var input = new string('a', 98) + "\U0001F600" + "b.w3x";

        Assert.That(TemporaryMapNaming.Sanitise(input), Is.EqualTo(new string('a', 98) + "\U0001F600"));
    }

    [Test]
    public void Sanitise_DropsCodeUnitsThatCannotBeNormalised()
    {
        // string.Normalize throws on unpaired surrogates and U+FFFE. A JSON "\ud800" escape in the
        // metadata part reaches here, so they are dropped rather than failing the upload with a 500.
        // Built in the body: attribute string arguments cannot carry an unpaired surrogate.
        Assert.That(TemporaryMapNaming.Sanitise("a\ud800b.w3x"), Is.EqualTo("ab"));
        Assert.That(TemporaryMapNaming.Sanitise("a\udc00b.w3x"), Is.EqualTo("ab"));
        Assert.That(TemporaryMapNaming.Sanitise("ab\ud83d.w3x"), Is.EqualTo("ab"));
        Assert.That(TemporaryMapNaming.Sanitise("a\ude00\ud83db.w3x"), Is.EqualTo("ab"));
        Assert.That(TemporaryMapNaming.Sanitise("a\ufffeb.w3x"), Is.EqualTo("ab"));
        Assert.That(TemporaryMapNaming.Sanitise("a\U0001F600b.w3x"), Is.EqualTo("a\U0001F600b"), "a well-formed pair is kept");
    }

    [TestCase("")]
    [TestCase(".w3x")]
    [TestCase("   .w3x")]
    [TestCase("///.w3x")]
    [TestCase(null)]
    public void Sanitise_FallsBackToMapWhenNothingSurvives(string input)
    {
        Assert.That(TemporaryMapNaming.Sanitise(input), Is.EqualTo("map"));
    }

    [Test]
    public void Sanitise_FallsBackToMap_WhenOnlyDotsAndSpacesSurviveTheTruncation()
    {
        // " . . . … ." fills the first 100 characters; the trailing "x" is cut off and the final trim
        // leaves nothing.
        var input = "\u00a0" + string.Concat(Enumerable.Repeat(". ", 60)) + "x.w3x";

        Assert.That(TemporaryMapNaming.Sanitise(input), Is.EqualTo("map"));
    }

    [TestCase("CON.w3x", "CON")]
    [TestCase("nul.W3X", "nul")]
    [TestCase("COM1.x.w3x", "COM1.x")]
    [TestCase("LPT1 .w3x", "LPT1")]
    public void Sanitise_KeepsWindowsDeviceNames_BecauseTheSpecOnlyRemovesTheReservedCharacters(string input, string expected)
    {
        Assert.That(TemporaryMapNaming.Sanitise(input), Is.EqualTo(expected));
    }

    [Test]
    public void BuildFileKey_UsesOnlyTheFirstEightHexCharactersOfTheSha1()
    {
        var key = TemporaryMapNaming.BuildFileKey("x.w3x", Sha1, ".w3x");

        Assert.That(key, Is.EqualTo("W3Champions/CustomGames/x-94ec3bda.w3x"));
        Assert.That(key.StartsWith(TemporaryMapLimits.TempMapPathPrefix, StringComparison.Ordinal), Is.True);
        Assert.That(key.Contains('\\'), Is.False);
        Assert.That(key.StartsWith("maps/", StringComparison.Ordinal), Is.False);
    }

    [Test]
    public void BuildFileKey_LowercasesTheSha1Suffix()
    {
        Assert.That(TemporaryMapNaming.BuildFileKey("x.w3x", Sha1.ToUpperInvariant(), ".w3x"),
            Is.EqualTo("W3Champions/CustomGames/x-94ec3bda.w3x"));
    }

    [TestCase("..", "map")]
    [TestCase(".", "map")]
    [TestCase("...w3x", "map")]
    [TestCase(". /. \\..w3x", "map")]
    [TestCase("../x.w3x", "x")]
    [TestCase("../../../x.w3x", "x")]
    [TestCase("a/b.w3x", "ab")]
    [TestCase("a\\b.w3x", "ab")]
    [TestCase("/abs/path.w3x", "abspath")]
    [TestCase("C:\\Maps\\x.w3x", "CMapsx")]
    [TestCase(".staging/x.w3x", "stagingx")]
    [TestCase("..%2F..%2Fx.w3x", "%2F..%2Fx")]
    public void BuildFileKey_NeverEscapesThePrefixOrAddsASegment(string originalFileName, string expectedName)
    {
        var key = TemporaryMapNaming.BuildFileKey(originalFileName, Sha1, ".w3x");

        Assert.That(key, Is.EqualTo(Prefix + expectedName + Suffix));
        Assert.That(FindPathSafetyViolation(key), Is.Null);
    }

    [Test]
    public void BuildFileKey_YieldsOneSafeSegmentUnderThePrefix_ForEveryUtf16CodeUnit()
    {
        var failures = new List<string>();
        for (var codeUnit = 0; codeUnit <= char.MaxValue; codeUnit++)
        {
            var c = (char)codeUnit;
            string[] names = [$"{c}.w3x", $"x{c}y.w3x", new string('a', 99) + c + "b.w3x"];
            foreach (var name in names)
            {
                var violation = FindPathSafetyViolation(TemporaryMapNaming.BuildFileKey(name, Sha1, ".w3x"));
                if (violation != null)
                {
                    failures.Add($"U+{codeUnit:X4} at index {name.IndexOf(c)}: {violation}");
                }
            }
        }

        Assert.That(failures, Is.Empty, string.Join("; ", failures.Take(20)));
    }

    // ---- The other §6.4 spellings, derived from the fileKey ------------------------------------

    [Test]
    public void UpdateServiceFileName_IsTheFileKeyWithoutItsRoot()
    {
        Assert.That(TemporaryMapNaming.UpdateServiceFileName(Prefix + "Legion TD" + Suffix), Is.EqualTo("CustomGames/Legion TD" + Suffix));
    }

    [TestCase("CustomGames/x-94ec3bda.w3x")]
    [TestCase("w3champions/CustomGames/x-94ec3bda.w3x")]
    [TestCase("")]
    [TestCase(null)]
    public void UpdateServiceFileName_RefusesAnythingNotUnderTheRoot(string notAFileKey)
    {
        // A slice of a non-fileKey would send update-service a name outside the temporary folder.
        Assert.Throws<ArgumentException>(() => TemporaryMapNaming.UpdateServiceFileName(notAFileKey));
    }

    [Test]
    public void GameMapPath_IsTheBackslashedFileKeyUnderTheMapsRoot()
    {
        Assert.That(TemporaryMapNaming.GameMapPath(Prefix + "Legion TD" + Suffix), Is.EqualTo(@"maps\W3Champions\CustomGames\Legion TD" + Suffix));
    }

    [TestCase("CustomGames/x-94ec3bda.w3x")]
    [TestCase("W3Champions\\CustomGames\\x-94ec3bda.w3x")]
    [TestCase(null)]
    public void GameMapPath_RefusesAnythingThatIsNotAFileKey(string notAFileKey)
    {
        Assert.Throws<ArgumentException>(() => TemporaryMapNaming.GameMapPath(notAFileKey));
    }

    // ---- The strict fileKey shape a restore requires before writing (S-I2) -----------------------

    [TestCase("W3Champions/CustomGames/Legion TD-94ec3bda.w3x", true)]
    [TestCase("W3Champions/CustomGames/Legion TD-94ec3bda.W3X", true)]
    [TestCase("W3Champions/CustomGames/x.w3m", true)]
    [TestCase("W3Champions/CustomGames/a..b-94ec3bda.w3x", true, TestName = "IsFileKey keeps inner dots")]
    [TestCase("W3Champions/CustomGames/\u00e9\u00e8 \ufeff-94ec3bda.w3x", true, TestName = "IsFileKey keeps non-ASCII and format characters")]
    [TestCase("W3Champions/CustomGames/.w3x", true, TestName = "IsFileKey accepts a name that is only the extension")]
    [TestCase("W3Champions/CustomGames/x-94ec3bda.w3x/", false, TestName = "IsFileKey refuses a trailing separator")]
    [TestCase("W3Champions/CustomGames/sub/x-94ec3bda.w3x", false, TestName = "IsFileKey refuses a second segment")]
    [TestCase("W3Champions/CustomGames/x-94ec3bda.zip", false)]
    [TestCase("W3Champions/CustomGames/x-94ec3bda.w3x.exe", false)]
    [TestCase("W3Champions/CustomGames/x-94ec3bda", false)]
    [TestCase("W3Champions/CustomGames/", false)]
    [TestCase("W3Champions/CustomGames/x-94ec3bda.w3x ", false, TestName = "IsFileKey refuses a trailing space after the extension")]
    [TestCase("W3Champions/CustomGames/x\u0000y-94ec3bda.w3x", false, TestName = "IsFileKey refuses U+0000")]
    [TestCase("W3Champions/CustomGames/x\u001fy-94ec3bda.w3x", false, TestName = "IsFileKey refuses U+001F")]
    [TestCase("W3Champions/CustomGames/x\ny-94ec3bda.w3x", false, TestName = "IsFileKey refuses a line feed")]
    [TestCase("W3Champions/CustomGames/x\u007fy-94ec3bda.w3x", true, TestName = "IsFileKey keeps U+007F (only < U+0020 is refused)")]
    [TestCase("W3Champions/v10/EchoIsles.w3x", false)]
    [TestCase("W3Champions/CustomGames/../v10/EchoIsles.w3x", false)]
    [TestCase("w3champions/CustomGames/x-94ec3bda.w3x", false)]
    [TestCase("W3Champions\\CustomGames\\x-94ec3bda.w3x", false)]
    [TestCase("W3Champions/CustomGames/a\\b-94ec3bda.w3x", false, TestName = "IsFileKey refuses a backslash inside the name")]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsFileKey_RequiresThePrefixOneSegmentAMapExtensionAndNoControlCharacter(string path, bool expected)
    {
        Assert.That(TemporaryMapNaming.IsFileKey(path), Is.EqualTo(expected));
    }

    [Test]
    public void EveryBuiltFileKey_IsAFileKey_ForEveryUtf16CodeUnit()
    {
        for (var codeUnit = 0; codeUnit <= char.MaxValue; codeUnit++)
        {
            var c = (char)codeUnit;
            foreach (var name in new[] { $"{c}.w3x", $"x{c}y.w3m", new string('a', 99) + c + "b.w3x" })
            {
                var key = TemporaryMapNaming.BuildFileKey(name, Sha1, name.EndsWith(".w3m", StringComparison.Ordinal) ? ".w3m" : ".w3x");
                Assert.That(TemporaryMapNaming.IsFileKey(key), Is.True, $"U+{codeUnit:X4}: {key}");
            }
        }
    }

    // ---- Control characters removed from the forwarded originalFileName (S-L5) -------------------

    [Test]
    public void RemoveControlCharacters_DropsC0AndC1AndKeepsEverythingElse()
    {
        var input = "\u0000a\u001f\u0020b\u007e\u007f\u0080c\u009f\u00a0d\u2028\ufeff/\\:*?\"<>|.w3x";

        Assert.That(TemporaryMapNaming.RemoveControlCharacters(input), Is.EqualTo("a\u0020b\u007ec\u00a0d\u2028\ufeff/\\:*?\"<>|.w3x"),
            "only U+0000-U+001F and U+007F-U+009F go; separators, the reserved set and other whitespace stay");
        Assert.That(TemporaryMapNaming.RemoveControlCharacters(null), Is.EqualTo(""));
        Assert.That(TemporaryMapNaming.RemoveControlCharacters("plain.w3x"), Is.SameAs("plain.w3x").Or.EqualTo("plain.w3x"));
    }

    [TestCase("x.w3m.w3x", "x.w3m", ".w3x")]
    [TestCase("x.W3x.W3M", "x.W3x", ".w3m")]
    [TestCase("x.w3x.w3x", "x.w3x", ".w3x")]
    public void DoubleExtensions_StripOnlyTheFinalMapExtension(string input, string expectedName, string expectedExtension)
    {
        Assert.That(TemporaryMapNaming.TryGetExtension(input, out var extension), Is.True);
        Assert.That(extension, Is.EqualTo(expectedExtension));
        Assert.That(TemporaryMapNaming.BuildFileKey(input, Sha1, extension),
            Is.EqualTo($"{Prefix}{expectedName}-94ec3bda{expectedExtension}"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("w3x")]
    [TestCase(".zip")]
    [TestCase(".W3X")]
    [TestCase(".w3x/")]
    [TestCase("/../../x.w3x")]
    public void BuildFileKey_RejectsAnExtensionThatDidNotComeFromTryGetExtension(string extension)
    {
        Assert.That(() => TemporaryMapNaming.BuildFileKey("x.w3x", Sha1, extension),
            Throws.InstanceOf<ArgumentException>().With.Property(nameof(ArgumentException.ParamName)).EqualTo("extension"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("94ec3bda")]
    [TestCase("94ec3bda2d9edd74bac37ec48edf4eb2b05138d")]
    [TestCase("94ec3bda2d9edd74bac37ec48edf4eb2b05138df0")]
    [TestCase("g4ec3bda2d9edd74bac37ec48edf4eb2b05138df")]
    [TestCase("../../../../../../../../../../../../../.")]
    public void BuildFileKey_RejectsASha1ThatIsNot40HexCharacters(string sha1)
    {
        Assert.That(() => TemporaryMapNaming.BuildFileKey("x.w3x", sha1, ".w3x"),
            Throws.InstanceOf<ArgumentException>().With.Property(nameof(ArgumentException.ParamName)).EqualTo("sha1"));
    }

    [TestCase("x.w3x", true, ".w3x")]
    [TestCase("x.W3X", true, ".w3x")]
    [TestCase("x.w3m", true, ".w3m")]
    [TestCase("x.W3m", true, ".w3m")]
    [TestCase("x.zip", false, null)]
    [TestCase("x", false, null)]
    [TestCase(null, false, null)]
    [TestCase("x.w3x.zip", false, null)]
    [TestCase("x.w3x ", false, null)]
    public void TryGetExtension_AcceptsOnlyW3xAndW3m(string input, bool expected, string expectedExtension)
    {
        Assert.That(TemporaryMapNaming.TryGetExtension(input, out var extension), Is.EqualTo(expected));
        Assert.That(extension, Is.EqualTo(expectedExtension));
    }

    [Test]
    public void TryGetExtension_ComparesOrdinally_SoATrailingIgnorableCharacterIsNoExtension()
    {
        // A culture-sensitive EndsWith gives U+FEFF and U+00AD zero weight and would accept these.
        Assert.That(TemporaryMapNaming.TryGetExtension("x.w3x\ufeff", out _), Is.False);
        Assert.That(TemporaryMapNaming.TryGetExtension("x.w3m\u00ad", out _), Is.False);
    }

    /// <summary>
    /// null when <paramref name="key"/> is the prefix plus exactly one segment "&lt;name&gt;-94ec3bda.w3x"
    /// whose name satisfies every §6.4 output property; otherwise the first violated property.
    /// </summary>
    private static string FindPathSafetyViolation(string key)
    {
        if (!key.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return "missing prefix";
        }

        var segment = key[Prefix.Length..];
        if (!segment.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return "missing sha1_8 suffix";
        }

        var name = segment[..^Suffix.Length];
        if (name.Length is < 1 or > TemporaryMapLimits.MaxSanitisedNameLength)
        {
            return $"name length {name.Length}";
        }

        if (name[0] is '.' or ' ' || name[^1] is '.' or ' ')
        {
            return "untrimmed dot or space";
        }

        if (name.Contains("  ", StringComparison.Ordinal))
        {
            return "uncollapsed spaces";
        }

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsHighSurrogate(c) && i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]))
            {
                i++;
                continue;
            }

            if (c < '\u0020' || (c >= '\u007f' && c <= '\u009f'))
            {
                return "control character";
            }

            if ("<>:\"/\\|?*".Contains(c))
            {
                return "reserved character";
            }

            if (c != ' ' && char.IsWhiteSpace(c))
            {
                return "whitespace other than U+0020";
            }

            if (char.IsSurrogate(c) || c == '\ufffe')
            {
                return "code unit that cannot be normalised";
            }
        }

        return null;
    }
}
