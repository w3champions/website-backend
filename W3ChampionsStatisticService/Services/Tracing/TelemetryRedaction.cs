using System;
using System.Text;
using W3C.Domain.Maps;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Services.Tracing;

/// <summary>
/// Removes secrets from telemetry values. Temporary-map secrets (design spec §10.3: never log a mapProof or a
/// proofHash) travel in a request header and a POST body since revision 10 — the x-proof-hash header of
/// website-backend's pre-check and the body of matchmaking's by-proof-hash lookup — because the proxies in front of
/// the services record request lines and not headers or bodies. No instrumentation here records headers or bodies
/// either; <see cref="IsSecretHeader"/> names the credential headers for everything that lists headers, and
/// <see cref="IsSecretHeaderKey"/> the attribute names an instrumentation would record them under. The URL
/// redaction stays for the shapes that used to carry the proofHash (a <c>/by-proof-hash/{proofHash}</c> path segment,
/// a <c>?proofHash=</c> query) and for the credentials still sent as query parameters: the SignalR hub's
/// <c>access_token</c>, the replay-service admin secret as <c>secret</c> and the caller's JWT to identification-service
/// as <c>authorization</c>. Values become "Redacted", the placeholder OpenTelemetry's own query redaction uses.
/// </summary>
public static class TelemetryRedaction
{
    public const string Redacted = "Redacted";

    private const string ProofHashPathPrefix = "/maps/temporary/by-proof-hash/";
    private const string RequestHeaderTagPrefix = "http.request.header.";
    private const string ResponseHeaderTagPrefix = "http.response.header.";

    private static readonly string[] SecretQueryKeys = ["proofHash", "mapProof", "access_token", "secret", "authorization"];

    /// <summary>
    /// Every header that carries a credential, lowercase: the standard ones, the admin secret the internal-service
    /// clients send, the pre-check's proofHash, the map-download key of update-service (spec A.7, not sent by this
    /// service), the API token of the rate limiter and chat-service's relationships secret.
    /// </summary>
    private static readonly string[] SecretHeaderNames =
    [
        "authorization",
        "proxy-authorization",
        "cookie",
        "set-cookie",
        "x-admin-secret",
        TemporaryMapKeys.ProofHashHeaderName,
        "x-map-key",
        "x-api-token",
        ChatServiceSecretAuthFilter.HeaderName,
    ];

    /// <summary>
    /// Whether <paramref name="name"/> is a credential header. Case-insensitive, as header names are on the wire, and
    /// an underscore counts as a dash, as the OpenTelemetry semantic conventions once spelled header attributes.
    /// </summary>
    public static bool IsSecretHeader(string name) => name != null && IsSecretHeader(name.AsSpan());

    /// <summary>
    /// Whether <paramref name="key"/> is where a telemetry item would hold a credential header's value: the
    /// semantic-convention attribute <c>http.request.header.{name}</c> or <c>http.response.header.{name}</c>, or
    /// the bare header name as a property dump would use it.
    /// </summary>
    public static bool IsSecretHeaderKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (key.StartsWith(RequestHeaderTagPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return IsSecretHeader(key.AsSpan(RequestHeaderTagPrefix.Length));
        }

        if (key.StartsWith(ResponseHeaderTagPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return IsSecretHeader(key.AsSpan(ResponseHeaderTagPrefix.Length));
        }

        return IsSecretHeader(key.AsSpan());
    }

    private static bool IsSecretHeader(ReadOnlySpan<char> name)
    {
        foreach (var secretHeader in SecretHeaderNames)
        {
            if (HeaderNameEquals(name, secretHeader))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><paramref name="secretHeader"/> is lowercase with dashes; <paramref name="name"/> may use any case and underscores.</summary>
    private static bool HeaderNameEquals(ReadOnlySpan<char> name, string secretHeader)
    {
        if (name.Length != secretHeader.Length)
        {
            return false;
        }

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i] == '_' ? '-' : char.ToLowerInvariant(name[i]);
            if (c != secretHeader[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Redacts a full URL, a bare path, or text embedding one (e.g. "GET /path"). Returns the same instance when
    /// there is nothing to redact.
    /// </summary>
    public static string RedactUrl(string url) => RedactUrl(url, everyQueryValue: false);

    /// <summary>
    /// Like <see cref="RedactUrl(string)"/>, but redacts the value of every query parameter and keeps the keys, as
    /// OpenTelemetry's HttpClient instrumentation does by default: for outbound URLs, whose query can carry a
    /// credential under any key. Returns the same instance when there is nothing to redact.
    /// </summary>
    public static string RedactUrlQueryValues(string url) => RedactUrl(url, everyQueryValue: true);

    private static string RedactUrl(string url, bool everyQueryValue)
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
        var redactedQuery = RedactQuery(query, everyQueryValue);
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
    public static string RedactQuery(string query) => RedactQuery(query, everyValue: false);

    private static string RedactQuery(string query, bool everyValue)
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
            if (separator > pairStart && (everyValue || IsSecretKey(query.AsSpan(pairStart, separator - pairStart))))
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
