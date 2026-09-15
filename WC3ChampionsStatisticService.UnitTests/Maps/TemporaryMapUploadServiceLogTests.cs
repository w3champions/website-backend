using System;
using System.Linq;
using System.Net;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// Upstream-supplied strings in log lines (S2-2, round 3): the Serilog file sink renders strings raw, so a fileState or
/// sha1 that matchmaking or update-service sends is rendered only when it has the shape the service expects, and the
/// placeholder "invalid" stands in otherwise. The same rule for paths is pinned in the failure tests.
/// </summary>
[TestFixture]
public class TemporaryMapUploadServiceLogTests : TemporaryMapUploadServiceTestBase
{
    /// <summary>CR LF and a fake log line, as JSON escapes spliced into a body.</summary>
    private const string LineBreak = "\\u000d\\u000aforged line";

    [TestCase("fileState of the dedupe record")]
    [TestCase("fileState of the winner after a failed create")]
    [TestCase("sha1 of the record claiming the path")]
    [TestCase("sha1 update-service parsed")]
    public void AnUpstreamStringWithALineBreak_IsLoggedAsInvalid(string site)
    {
        var handler = site switch
        {
            "fileState of the dedupe record" => new ScriptedHttpHandler()
                .On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811, fileState: "gone" + LineBreak))),
            "fileState of the winner after a failed create" => OnSequence(new ScriptedHttpHandler(), IsBySha1,
                    Respond(HttpStatusCode.NotFound), Respond(HttpStatusCode.OK, Record(99, fileState: "gone" + LineBreak)))
                .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()))
                .On(IsCreate, Respond(HttpStatusCode.BadRequest, "{}")),
            "sha1 of the record claiming the path" => UnknownSha1Handler()
                .On(IsUsUpload, Respond(HttpStatusCode.Conflict, "{\"message\":\"File already exists\"}"))
                .On(IsByPath, Respond(HttpStatusCode.OK, Record(4242, sha1: "abc" + LineBreak))),
            _ => UnknownSha1Handler()
                .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody(metaSha1: "abc" + LineBreak))),
        };
        handler.On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        using var logs = new LogCapture();

        Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, logger: logs.Logger));

        Assert.That(logs.Lines().Any(l => l.Contains("forged", StringComparison.Ordinal)), Is.False, "the value was rendered as sent");
        Assert.That(logs.Lines().Count(l => l.Contains("=\"invalid\"", StringComparison.Ordinal)), Is.EqualTo(1),
            "the placeholder stands in for the value, as the property value too");
    }

    [TestCase("expired")]
    [TestCase("Not_Present-yet")]
    [TestCase("12345678901234567890123456789012", TestName = "AnUnknownFileState_Of32Characters_IsLoggedAsSent")]
    public void AnUnknownFileStateThatIsAPlainToken_IsLoggedAsSent(string fileState)
    {
        // The unknown value is the point of that warning, so it stays readable within [0-9A-Za-z_-]{1,32}.
        var handler = new ScriptedHttpHandler().On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811, fileState: fileState)));
        using var logs = new LogCapture();

        Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, logger: logs.Logger));

        Assert.That(logs.Lines().Where(l => l.StartsWith("Warning") && l.Contains("FileState=\"" + fileState + "\"", StringComparison.Ordinal)),
            Has.Exactly(1).Items);
    }

    [TestCase("gone.", TestName = "AnUnknownFileState_WithADot_IsLoggedAsInvalid")]
    [TestCase("gone+", TestName = "AnUnknownFileState_WithAPlus_IsLoggedAsInvalid")]
    [TestCase("gon\u00e9", TestName = "AnUnknownFileState_WithALetterOutsideAscii_IsLoggedAsInvalid")]
    [TestCase("gone forged", TestName = "AnUnknownFileState_WithASpace_IsLoggedAsInvalid")]
    [TestCase("123456789012345678901234567890123", TestName = "AnUnknownFileState_Of33Characters_IsLoggedAsInvalid")]
    public void AnUnknownFileStateOutsideThePlainAlphabet_IsLoggedAsInvalid(string fileState)
    {
        var handler = new ScriptedHttpHandler().On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811, fileState: fileState)));
        using var logs = new LogCapture();

        Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, logger: logs.Logger));

        Assert.That(logs.Lines().Count(l => l.Contains("FileState=\"invalid\"", StringComparison.Ordinal)), Is.EqualTo(1));
        Assert.That(logs.Lines().Any(l => l.Contains(fileState, StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void ASha1UpdateServiceParsed_IsLoggedAsSent_WhenItIs40LowercaseHex()
    {
        var handler = UnknownSha1Handler()
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody(metaSha1: OtherSha1)))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        using var logs = new LogCapture();

        var ex = Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, logger: logs.Logger));

        Assert.That(ex.Code, Is.EqualTo("PARSER_MISMATCH"));
        Assert.That(logs.Lines().Where(l => l.StartsWith("Warning") && l.Contains("ParsedSha1=\"" + OtherSha1 + "\"", StringComparison.Ordinal)),
            Has.Exactly(1).Items);
    }

    [TestCase("0123456789ABCDEF0123456789ABCDEF01234567", TestName = "AParsedSha1_InUppercase_IsLoggedLowercased")]
    public void AParsedSha1_IsLoggedInTheLowercaseTheServiceCompares(string metaSha1)
    {
        // The service lower-cases the parsed digest before comparing, so the log shows what was compared.
        var handler = UnknownSha1Handler()
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody(metaSha1: metaSha1)))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        using var logs = new LogCapture();

        Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, logger: logs.Logger));

        Assert.That(logs.Lines().Where(l => l.Contains("ParsedSha1=\"" + metaSha1.ToLowerInvariant() + "\"", StringComparison.Ordinal)),
            Has.Exactly(1).Items);
    }

    [TestCase("abc", TestName = "AParsedSha1_TooShort_IsLoggedAsInvalid")]
    [TestCase("0123456789abcdef0123456789abcdef0123456", TestName = "AParsedSha1_Of39Hex_IsLoggedAsInvalid")]
    [TestCase("0123456789abcdef0123456789abcdef0123456g", TestName = "AParsedSha1_WithANonHexCharacter_IsLoggedAsInvalid")]
    public void AParsedSha1OfTheWrongShape_IsLoggedAsInvalid(string metaSha1)
    {
        var handler = UnknownSha1Handler()
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody(metaSha1: metaSha1)))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        using var logs = new LogCapture();

        Assert.ThrowsAsync<TemporaryMapUploadException>(() => Run(handler, logger: logs.Logger));

        Assert.That(logs.Lines().Count(l => l.Contains("ParsedSha1=\"invalid\"", StringComparison.Ordinal)), Is.EqualTo(1));
    }
}
