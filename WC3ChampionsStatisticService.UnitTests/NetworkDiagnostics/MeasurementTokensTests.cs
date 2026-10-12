using System.Linq;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class MeasurementTokensTests
{
    [Test]
    public void NewToken_Is43CharBase64Url_AndRandom()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => MeasurementTokens.NewToken()).ToList();

        Assert.That(tokens, Has.All.Match("^[A-Za-z0-9_-]{43}$"), "256 bits, base64url, no padding");
        Assert.AreEqual(tokens.Count, tokens.Distinct().Count());
    }

    [Test]
    public void Matches_OnlyTheExactStoredToken()
    {
        var token = MeasurementTokens.NewToken();

        Assert.IsTrue(MeasurementTokens.Matches(token, token));
        Assert.IsTrue(MeasurementTokens.Matches(token, new string(token.ToCharArray())), "value equality, not reference equality");
        Assert.IsFalse(MeasurementTokens.Matches(token, token[..^1] + (token[^1] == 'A' ? 'B' : 'A')), "one character off");
        Assert.IsFalse(MeasurementTokens.Matches(token, token[..^1]), "too short");
        Assert.IsFalse(MeasurementTokens.Matches(token, token + "A"), "too long");
        Assert.IsFalse(MeasurementTokens.Matches(token, new string('A', 500)));
        Assert.IsFalse(MeasurementTokens.Matches(token, null));
        Assert.IsFalse(MeasurementTokens.Matches(token, ""));
        Assert.IsFalse(MeasurementTokens.Matches(null, token), "an assignment without a token never matches");
        Assert.IsFalse(MeasurementTokens.Matches(null, null));
    }
}
