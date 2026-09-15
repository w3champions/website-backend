using System;
using System.Text;

namespace W3ChampionsStatisticService.Services.Tracing;

/// <summary>
/// Removes secrets from URL-shaped telemetry values. Temporary-map secrets (design spec §10.3: never log a mapProof or
/// a proofHash) reach telemetry in two shapes: matchmaking's <c>/maps/temporary/by-proof-hash/{proofHash}</c> route
/// carries the proofHash as a path segment, and website-backend's own routes take it as a query parameter. The
/// SignalR hub takes its token as the <c>access_token</c> query parameter. Values become "Redacted", the
/// placeholder OpenTelemetry's own query redaction uses.
/// </summary>
public static class TelemetryRedaction
{
    public const string Redacted = "Redacted";

    private const string ProofHashPathPrefix = "/maps/temporary/by-proof-hash/";

    private static readonly string[] SecretQueryKeys = ["proofHash", "mapProof", "access_token"];

    /// <summary>
    /// Redacts a full URL, a bare path, or text embedding one (e.g. "GET /path"). Returns the same instance when
    /// there is nothing to redact.
    /// </summary>
    public static string RedactUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return url;
        }

        var fragmentStart = url.IndexOf('#');
        var queryEnd = fragmentStart >= 0 ? fragmentStart : url.Length;
        var queryStart = url.IndexOf('?', 0, queryEnd);
        var pathEnd = queryStart >= 0 ? queryStart : queryEnd;

        var redactedPath = RedactProofHashSegments(url, pathEnd);
        var query = url[pathEnd..queryEnd];
        var redactedQuery = RedactQuery(query);
        if (redactedPath == null && ReferenceEquals(redactedQuery, query))
        {
            return url;
        }

        return (redactedPath ?? url[..pathEnd]) + redactedQuery + url[queryEnd..];
    }

    /// <summary>
    /// Redacts the values of secret-bearing keys in a query string, with or without its leading '?'. Keys match
    /// the way ASP.NET Core binds them: case-insensitively, after decoding. Returns the same instance when there
    /// is nothing to redact.
    /// </summary>
    public static string RedactQuery(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return query;
        }

        StringBuilder result = null;
        var copiedUpTo = 0;
        var pairStart = query[0] == '?' ? 1 : 0;
        while (pairStart < query.Length)
        {
            var pairEnd = query.IndexOf('&', pairStart);
            if (pairEnd < 0)
            {
                pairEnd = query.Length;
            }

            var separator = query.IndexOf('=', pairStart, pairEnd - pairStart);
            if (separator > pairStart && IsSecretKey(query.AsSpan(pairStart, separator - pairStart)))
            {
                var valueStart = separator + 1;
                if (NeedsRedaction(query.AsSpan(valueStart, pairEnd - valueStart)))
                {
                    result ??= new StringBuilder(query.Length);
                    result.Append(query, copiedUpTo, valueStart - copiedUpTo).Append(Redacted);
                    copiedUpTo = pairEnd;
                }
            }

            pairStart = pairEnd + 1;
        }

        return result == null ? query : result.Append(query, copiedUpTo, query.Length - copiedUpTo).ToString();
    }

    /// <summary>The path part (<c>url[..pathEnd]</c>) with every proofHash segment redacted, or null if unchanged.</summary>
    private static string RedactProofHashSegments(string url, int pathEnd)
    {
        StringBuilder result = null;
        var copiedUpTo = 0;
        var searchFrom = 0;
        while (searchFrom < pathEnd)
        {
            var prefixStart = url.IndexOf(ProofHashPathPrefix, searchFrom, pathEnd - searchFrom, StringComparison.OrdinalIgnoreCase);
            if (prefixStart < 0)
            {
                break;
            }

            var segmentStart = prefixStart + ProofHashPathPrefix.Length;
            var segmentEnd = url.IndexOf('/', segmentStart, pathEnd - segmentStart);
            if (segmentEnd < 0)
            {
                segmentEnd = pathEnd;
            }

            if (NeedsRedaction(url.AsSpan(segmentStart, segmentEnd - segmentStart)))
            {
                result ??= new StringBuilder(pathEnd);
                result.Append(url, copiedUpTo, segmentStart - copiedUpTo).Append(Redacted);
                copiedUpTo = segmentEnd;
            }

            searchFrom = segmentEnd;
        }

        return result?.Append(url, copiedUpTo, pathEnd - copiedUpTo).ToString();
    }

    private static bool NeedsRedaction(ReadOnlySpan<char> value) => !value.IsEmpty && !value.SequenceEqual(Redacted);

    private static bool IsSecretKey(ReadOnlySpan<char> key)
    {
        var decoded = key.IndexOfAny('%', '+') >= 0
            ? Uri.UnescapeDataString(key.ToString().Replace('+', ' ')).AsSpan()
            : key;
        foreach (var secretKey in SecretQueryKeys)
        {
            if (decoded.Equals(secretKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
