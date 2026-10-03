using System;
using System.Text;
using NUnit.Framework;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// Pins the normative mapProof definition of design spec §10.2. The whole private-download scheme
/// hangs off these three strings agreeing byte-for-byte with launcher-e (Rust) and update-service.
/// </summary>
[TestFixture]
public class MapProofTests
{
    // printf 'w3champions-map-proof-v1\nabc' | shasum -a 256
    private const string AbcMapProof = "3b2724682f427727f8fa19236e04d3a5e3bdf218b7fe9c259c202e5264edbf4e";
    // printf 'abc' | shasum -a 1
    private const string AbcSha1 = "a9993e364706816aba3e25717850c26c9cd0d89d";
    // printf '3b2724...bf4e' | shasum -a 256
    private const string AbcProofHash = "6e60c9a01cfd09cec0b8493fe1ad9f2411ed21a1e1e58aab9eceeb3e9bac2bd6";

    [Test]
    public void Prefix_IsExactly24AsciiBytesPlusOneLineFeed()
    {
        var bytes = Encoding.UTF8.GetBytes(MapProof.Prefix);

        Assert.That(bytes.Length, Is.EqualTo(25));
        Assert.That(bytes[24], Is.EqualTo((byte)0x0a));
        Assert.That(MapProof.Prefix, Is.EqualTo("w3champions-map-proof-v1\n"));
    }

    [Test]
    public void Hasher_ProducesTheSpecVectorFor_abc()
    {
        using var hasher = new MapProofHasher();
        hasher.Append(Encoding.UTF8.GetBytes("abc"));

        var (sha1, mapProof) = hasher.Finish();

        Assert.That(sha1, Is.EqualTo(AbcSha1));
        Assert.That(mapProof, Is.EqualTo(AbcMapProof));
        Assert.That(hasher.BytesHashed, Is.EqualTo(3));
    }

    [Test]
    public void Hasher_IsChunkBoundaryIndependent()
    {
        using var oneShot = new MapProofHasher();
        oneShot.Append(Encoding.UTF8.GetBytes("abc"));

        using var chunked = new MapProofHasher();
        chunked.Append(Encoding.UTF8.GetBytes("a"));
        chunked.Append(Encoding.UTF8.GetBytes("b"));
        chunked.Append(Encoding.UTF8.GetBytes("c"));

        Assert.That(chunked.Finish(), Is.EqualTo(oneShot.Finish()));
    }

    [Test]
    public void Hasher_FinishTwice_Throws()
    {
        // IncrementalHash resets on GetHashAndReset, so a second Finish would silently return the
        // hash of an empty, prefix-less input: a wrong proof that only surfaces upstream.
        using var hasher = new MapProofHasher();
        hasher.Append(Encoding.UTF8.GetBytes("abc"));
        hasher.Finish();

        Assert.Throws<InvalidOperationException>(() => hasher.Finish());
    }

    [Test]
    public void Hasher_AppendAfterFinish_Throws()
    {
        using var hasher = new MapProofHasher();
        hasher.Append(Encoding.UTF8.GetBytes("abc"));
        hasher.Finish();

        Assert.Throws<InvalidOperationException>(() => hasher.Append(Encoding.UTF8.GetBytes("d")));
        Assert.That(hasher.BytesHashed, Is.EqualTo(3), "a rejected append must not count its bytes");
    }

    [Test]
    public void Hash_RoundTripsTheSpecVector()
    {
        Assert.That(MapProof.Hash(AbcMapProof), Is.EqualTo(AbcProofHash));
    }

    [TestCase("3b2724682f427727f8fa19236e04d3a5e3bdf218b7fe9c259c202e5264edbf4e", 64, true)]
    [TestCase("3B2724682F427727F8FA19236E04D3A5E3BDF218B7FE9C259C202E5264EDBF4E", 64, false)]
    [TestCase("3b2724", 64, false)]
    [TestCase("3b2724682f427727f8fa19236e04d3a5e3bdf218b7fe9c259c202e5264edbf4e0", 64, false)]
    [TestCase(null, 64, false)]
    [TestCase("", 64, false)]
    [TestCase("a9993e364706816aba3e25717850c26c9cd0d89d", 40, true)]
    [TestCase("g9993e364706816aba3e25717850c26c9cd0d89d", 40, false)]
    [TestCase("/9993e364706816aba3e25717850c26c9cd0d89d", 40, false)]
    [TestCase(":9993e364706816aba3e25717850c26c9cd0d89d", 40, false)]
    [TestCase("`9993e364706816aba3e25717850c26c9cd0d89d", 40, false)]
    public void IsLowercaseHex_AcceptsOnlyExactLowercaseHexOfTheGivenLength(string value, int length, bool expected)
    {
        Assert.That(MapProof.IsLowercaseHex(value, length), Is.EqualTo(expected));
    }
}
