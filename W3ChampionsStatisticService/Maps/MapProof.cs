using System;
using System.Security.Cryptography;
using System.Text;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// The possession proof of design spec §10.2:
/// <c>mapProof = lowercase hex SHA-256( UTF-8("w3champions-map-proof-v1\n") || file bytes )</c> and
/// <c>proofHash = lowercase hex SHA-256( UTF-8(mapProof) )</c>.
/// <para>
/// The prefix is a constant, not a secret; the security property is NON-EMISSION. NEVER log a
/// mapProof, a proofHash or an update-service MapProofHash (§10.3). sha1 may be logged.
/// </para>
/// </summary>
public static class MapProof
{
    /// <summary>Exactly the 24 ASCII bytes "w3champions-map-proof-v1" followed by one line feed.</summary>
    public const string Prefix = "w3champions-map-proof-v1\n";

    /// <summary>Derives the lookup key from a proof. Never log either value.</summary>
    public static string Hash(string mapProof)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mapProof))).ToLowerInvariant();

    public static bool IsLowercaseHex(string value, int length)
    {
        if (value == null || value.Length != length)
        {
            return false;
        }

        foreach (var c in value)
        {
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Computes sha1 and mapProof over the same byte stream in one pass, so a 256 MiB upload is hashed
/// while it is spooled and never held in memory. Single use: <see cref="Finish"/> may be called once,
/// and nothing may be appended after it.
/// </summary>
public sealed class MapProofHasher : IDisposable
{
    private readonly IncrementalHash _sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
    private readonly IncrementalHash _proof = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long _bytesHashed;
    private bool _finished;

    public MapProofHasher()
    {
        _proof.AppendData(Encoding.UTF8.GetBytes(MapProof.Prefix));
    }

    public long BytesHashed => _bytesHashed;

    public void Append(ReadOnlySpan<byte> chunk)
    {
        ThrowIfFinished();
        _sha1.AppendData(chunk);
        _proof.AppendData(chunk);
        _bytesHashed += chunk.Length;
    }

    /// <summary>
    /// Both values lowercase hex. The second one is a secret — never log it. Throws on a second call:
    /// the underlying hashes reset, so a repeat would silently yield a digest without the prefix.
    /// </summary>
    public (string Sha1, string MapProof) Finish()
    {
        ThrowIfFinished();
        _finished = true;
        return (ToHex(_sha1), ToHex(_proof));
    }

    public void Dispose()
    {
        _sha1.Dispose();
        _proof.Dispose();
    }

    private void ThrowIfFinished()
    {
        if (_finished)
        {
            throw new InvalidOperationException("MapProofHasher is single use and has already been finished.");
        }
    }

    private static string ToHex(IncrementalHash hash)
        => Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
}
