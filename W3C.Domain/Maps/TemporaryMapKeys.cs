using System;
using System.Linq;

namespace W3C.Domain.Maps;

/// <summary>
/// The key shapes of self-provided (temporary) custom maps, defined once for the matchmaking and update-service
/// clients and for website-backend's own map code (design spec §6.4 and §10.2). website-backend's
/// <c>MapProof.IsLowercaseHex</c> and <c>TemporaryMapLimits.TempMapPathPrefix</c> delegate here.
/// </summary>
public static class TemporaryMapKeys
{
    /// <summary>The single stored-file path prefix that makes a map "temporary". Forward slashes, case preserved.</summary>
    public const string PathPrefix = "W3Champions/CustomGames/";

    /// <summary>Length of a lowercase hex SHA-1 digest (a map file's sha1).</summary>
    public const int Sha1HexLength = 40;

    /// <summary>Length of a lowercase hex SHA-256 digest (a mapProof or a proofHash).</summary>
    public const int ProofHashHexLength = 64;

    /// <summary>
    /// The request header that carries the proofHash to website-backend's pre-check (design spec Appendix A.4,
    /// revision 10). A header rather than a query parameter because the proxies in front of the service record
    /// request lines in their logs and do not record headers; its value is a credential-derived secret and is never
    /// logged (§10.3).
    /// </summary>
    public const string ProofHashHeaderName = "x-proof-hash";

    /// <summary>
    /// true for a stored-file path under <see cref="PathPrefix"/> with no backslash and no empty, "." or ".." segment.
    /// Inner dots stay legal: the §6.4 names keep them (e.g. "a..b-94ec3bda.w3x"), so only whole segments are checked.
    /// </summary>
    public static bool IsFilePath(string path)
        => path != null
           && path.StartsWith(PathPrefix, StringComparison.Ordinal)
           && !path.Contains('\\')
           && !path.Split('/').Any(segment => segment is "" or "." or "..");

    /// <summary>true when <paramref name="value"/> is exactly <paramref name="length"/> characters of 0-9 and a-f.</summary>
    public static bool IsLowercaseHex(string value, int length)
    {
        if (value == null || value.Length != length)
        {
            return false;
        }

        foreach (var c in value)
        {
            var isHex = c is >= '0' and <= '9' or >= 'a' and <= 'f';
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }
}
