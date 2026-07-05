using System;
using NUnit.Framework;
using W3C.Domain.ChatService;

namespace WC3ChampionsStatisticService.Tests.Friend;

/// <summary>
/// Pinned interop coverage for <see cref="ChatInternalApiSigner"/>.
/// </summary>
[TestFixture]
public class ChatInternalApiSignerTests
{
    // ── Shared HMAC vectors ─────────────────────────────────────────────────────
    // Scheme (pinned cross-repo, C7's HmacSignatureVerifier is the counterpart):
    //   X-W3C-Webhook-Timestamp: <unix seconds>
    //   X-W3C-Signature: v1=hex(HMAC_SHA256(UTF8(secret), UTF8("v1." + ts + ".") ++ rawBodyBytes))
    // Vectors 1–2 are C7's published M1 interop vectors (recomputed, byte-identical);
    // vector 3 is W2's change-ping vector — replayable through chat-service's
    // HmacSignatureVerifierTests for cross-repo interop checks.
    [TestCase("{\"kind\":\"match\",\"ref\":\"abc123XYZ0\",\"name\":\"Test Lobby\",\"members\":[\"Foo#1234\",\"Bar#5678\"]}",
        "b0acb9b2ba23a8aaf0076c05cd1c9631ac88364dfcebe61352c220f9009e54cd")]
    [TestCase("", "09b6a138e0b80b2d6c4fa412590abcc352953b7e43ba15479020161e944f47a3")]
    [TestCase("{\"type\":\"block\",\"actor\":\"Foo#1234\",\"target\":\"Bar#5678\"}",
        "bedf415a037eaf3e79594fbe063af10678516d34adb2e5b59feafd181c8608a7")]
    public void CreateSignatureHeaderValue_MatchesPinnedVectors(string rawBody, string expectedHex)
    {
        var header = ChatInternalApiSigner.CreateSignatureHeaderValue("test-secret", "1751500000", rawBody);
        Assert.That(header, Does.StartWith("v1="));
        Assert.That(header["v1=".Length..], Is.EqualTo(expectedHex).IgnoreCase);
    }

    [Test]
    public void CreateSignatureHeaderValue_NullBody_MatchesEmptyBody()
    {
        var nullBodyHeader = ChatInternalApiSigner.CreateSignatureHeaderValue("test-secret", "1751500000", null);
        var emptyBodyHeader = ChatInternalApiSigner.CreateSignatureHeaderValue("test-secret", "1751500000", "");

        Assert.That(nullBodyHeader, Is.EqualTo(emptyBodyHeader));
    }

    [TestCase(null)]
    [TestCase("")]
    public void CreateSignatureHeaderValue_NullOrEmptySecret_Throws(string secret)
    {
        Assert.Catch<ArgumentException>(() =>
            ChatInternalApiSigner.CreateSignatureHeaderValue(secret, "1751500000", "{}"));
    }

    [TestCase(null)]
    [TestCase("")]
    public void CreateSignatureHeaderValue_NullOrEmptyTimestamp_Throws(string timestamp)
    {
        Assert.Catch<ArgumentException>(() =>
            ChatInternalApiSigner.CreateSignatureHeaderValue("test-secret", timestamp, "{}"));
    }

    [Test]
    public void TimestampHeaderName_IsPinned()
    {
        Assert.That(ChatInternalApiSigner.TimestampHeaderName, Is.EqualTo("X-W3C-Webhook-Timestamp"));
    }

    [Test]
    public void SignatureHeaderName_IsPinned()
    {
        Assert.That(ChatInternalApiSigner.SignatureHeaderName, Is.EqualTo("X-W3C-Signature"));
    }
}
