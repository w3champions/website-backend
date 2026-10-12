using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace W3ChampionsStatisticService.NetworkDiagnostics;

/// <summary>
/// Contract A10: a measurement token is 256 random bits, base64url without padding (43 chars), created with the
/// assignment and stored in plaintext on it. Every re-push sends the stored token; submit compares in constant time.
/// Never log a token and never put one in an admin/LLM DTO.
/// </summary>
public static class MeasurementTokens
{
    public const int TokenBytes = 32;
    private const int MaxPresentedLength = 64;

    public static string NewToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    public static bool Matches(string stored, string presented)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(presented) || presented.Length > MaxPresentedLength) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(presented));
    }
}
