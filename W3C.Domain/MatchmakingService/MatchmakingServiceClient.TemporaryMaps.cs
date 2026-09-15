using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Common;
using W3C.Domain.MatchmakingService.Contracts;
using W3C.Domain.Tracing;

namespace W3C.Domain.MatchmakingService;

/// <summary>
/// The x-admin-secret routes matchmaking-service exposes for self-provided (temporary) maps — design spec
/// §5.4 / Appendix A.5. Consumed only by website-backend; never log a mapProof or a proofHash (§10.3).
/// </summary>
public partial class MatchmakingServiceClient
{
    private const string ServiceName = "matchmaking-service";
    private const int Sha1HexLength = 40;
    private const int ProofHashHexLength = 64;

    private static readonly char[] JsonWhitespace = [' ', '\t', '\r', '\n'];

    // ---- Temporary (self-provided) maps -------------------------------------------------
    // All routes carry x-admin-secret and are consumed only by website-backend. 404 and 409 are
    // EXPECTED outcomes here, not errors, so these methods branch on the status code themselves.
    // A body that breaks the A.5 contract on any other expected status throws an HttpRequestException
    // carrying that status (see UpstreamContract). Never log a mapProof or a proofHash (design spec §10.3).

    /// <summary>Upload dedupe probe. Returns null when no temporary map has this sha1 (strict 404 {}).</summary>
    public async Task<MapContract> GetTemporaryMapBySha1(string sha1, CancellationToken cancellationToken = default)
    {
        RequireLowercaseHex(sha1, Sha1HexLength, nameof(sha1));
        var url = $"{MatchmakingApiUrl}/maps/temporary/by-sha1/{Uri.EscapeDataString(sha1)}";
        var response = await SendWithSecret(HttpMethod.Get, url, cancellationToken: cancellationToken);
        return (await ReadRecordOrNull<TemporaryMapEnvelope>(response, e => e.Map != null, cancellationToken))?.Map;
    }

    /// <summary>Pre-check probe. Returns null when nothing is known for this proofHash (strict 404 {}).</summary>
    public async Task<TemporaryMapStateResponse> GetTemporaryMapStateByProofHash(
        [NoTrace] string proofHash, CancellationToken cancellationToken = default)
    {
        RequireLowercaseHex(proofHash, ProofHashHexLength, nameof(proofHash));
        var url = $"{MatchmakingApiUrl}/maps/temporary/by-proof-hash/{Uri.EscapeDataString(proofHash)}";
        var response = await SendWithSecret(HttpMethod.Get, url, cancellationToken: cancellationToken);
        return await ReadRecordOrNull<TemporaryMapStateResponse>(response, s => !string.IsNullOrEmpty(s.FileState), cancellationToken);
    }

    /// <summary>Reconciliation probe: does a record still claim this stored file? Null means orphan (strict 404 {}).</summary>
    public async Task<MapContract> GetTemporaryMapByPath(string path, CancellationToken cancellationToken = default)
    {
        var url = $"{MatchmakingApiUrl}/maps/temporary/by-path?path={HttpUtility.UrlEncode(path)}";
        var response = await SendWithSecret(HttpMethod.Get, url, cancellationToken: cancellationToken);
        return (await ReadRecordOrNull<TemporaryMapEnvelope>(response, e => e.Map != null, cancellationToken))?.Map;
    }

    public async Task<CreateTemporaryMapResult> CreateTemporaryMap(
        [NoTrace] CreateTemporaryMapRequest request, CancellationToken cancellationToken = default)
    {
        var url = $"{MatchmakingApiUrl}/maps/temporary";
        var response = await SendWithSecret(HttpMethod.Post, url, SerializeData(request), cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return new CreateTemporaryMapResult { Created = false, Map = await ReadMap(response, cancellationToken) };
        }

        if (!response.IsSuccessStatusCode) await HandleMMError(response, cancellationToken);
        return new CreateTemporaryMapResult { Created = true, Map = await ReadMap(response, cancellationToken) };
    }

    /// <summary>Non-mutating proof lookup. Returns null when no record has this proofHash (strict 404 {}).</summary>
    public async Task<VerifyTemporaryMapProofResponse> VerifyTemporaryMapProof(
        [NoTrace] string proofHash, CancellationToken cancellationToken = default)
    {
        RequireLowercaseHex(proofHash, ProofHashHexLength, nameof(proofHash));
        var url = $"{MatchmakingApiUrl}/maps/temporary/verify-proof";
        var response = await SendWithSecret(HttpMethod.Post, url, SerializeData(new { proofHash }), cancellationToken);
        return await ReadRecordOrNull<VerifyTemporaryMapProofResponse>(response, v => !string.IsNullOrEmpty(v.FileState), cancellationToken);
    }

    /// <summary>Call ONLY after the bytes are stored in update-service: this flips fileState to present.</summary>
    public async Task<MapContract> MarkTemporaryMapFileRestored(
        int mapId, [NoTrace] TemporaryMapFileRestoredRequest request, CancellationToken cancellationToken = default)
    {
        var url = $"{MatchmakingApiUrl}/maps/temporary/{mapId.ToString(CultureInfo.InvariantCulture)}/file-restored";
        var response = await SendWithSecret(HttpMethod.Post, url, SerializeData(request), cancellationToken);
        if (!response.IsSuccessStatusCode) await HandleMMError(response, cancellationToken);
        return await ReadMap(response, cancellationToken);
    }

    public async Task<MapContract> MarkTemporaryMapFileDeleted(int mapId, CancellationToken cancellationToken = default)
    {
        var url = $"{MatchmakingApiUrl}/maps/temporary/{mapId.ToString(CultureInfo.InvariantCulture)}/file-deleted";
        var response = await SendWithSecret(HttpMethod.Post, url, cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode) await HandleMMError(response, cancellationToken);
        return await ReadMap(response, cancellationToken);
    }

    /// <summary>One page of expired records. A page without its items array throws rather than reading as empty.</summary>
    public async Task<ExpiredTemporaryMapsResponse> GetExpiredTemporaryMaps(
        long beforeEpochMs, int limit, CancellationToken cancellationToken = default)
    {
        var url = $"{MatchmakingApiUrl}/maps/temporary/expired" +
                  $"?before={beforeEpochMs.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}";
        var response = await SendWithSecret(HttpMethod.Get, url, cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode) await HandleMMError(response, cancellationToken);
        return await ReadContractBody<ExpiredTemporaryMapsResponse>(response, cancellationToken);
    }

    /// <summary>
    /// Spec §5.4 pins <c>404 {}</c> as the record-not-found answer of every null-returning probe. Any
    /// other 404 (an HTML "Cannot GET" page, a proxy page, a non-empty body) means the ROUTE was not found,
    /// and reading it as "no record" would create duplicate records or delete claimed files — so it throws.
    /// A success without the record (<paramref name="hasRecord"/> false) is an upstream fault, never "no record".
    /// </summary>
    private async Task<T> ReadRecordOrNull<T>(HttpResponseMessage response, Func<T, bool> hasRecord, CancellationToken cancellationToken)
        where T : class
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (IsEmptyJsonObject(body)) return null;
            throw new HttpRequestException(
                $"{ServiceName} answered 404 without the empty-object record-not-found body", null, HttpStatusCode.NotFound);
        }

        if (!response.IsSuccessStatusCode) await HandleMMError(response, cancellationToken);
        var record = await ReadContractBody<T>(response, cancellationToken);
        return hasRecord(record) ? record : throw UpstreamContract.Violation(response.StatusCode, ServiceName);
    }

    /// <summary>
    /// sha1 and proofHash are lowercase hex digests, and two routes carry them as a URL path segment, where System.Uri
    /// would collapse a "." or ".." key into another route. Anything else is refused before a request is built; the
    /// message never echoes the value. Mirrors <c>MapProof.IsLowercaseHex</c>, which lives in the web project that
    /// W3C.Domain cannot reference.
    /// </summary>
    private static void RequireLowercaseHex(string value, int length, string parameterName)
    {
        if (value == null || value.Length != length || !value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new ArgumentException($"must be {length} lowercase hex characters", parameterName);
        }
    }

    private static bool IsEmptyJsonObject(string body)
    {
        var trimmed = (body ?? "").AsSpan().Trim(JsonWhitespace);
        return trimmed.Length >= 2
               && trimmed[0] == '{'
               && trimmed[^1] == '}'
               && trimmed[1..^1].Trim(JsonWhitespace).IsEmpty;
    }

    /// <summary>The <c>{ "map": ... }</c> envelope every mutating temporary-map route answers with.</summary>
    private async Task<MapContract> ReadMap(HttpResponseMessage response, CancellationToken cancellationToken)
        => (await ReadContractBody<TemporaryMapEnvelope>(response, cancellationToken)).Map
           ?? throw UpstreamContract.Violation(response.StatusCode, ServiceName);

    private static async Task<T> ReadContractBody<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
        => UpstreamContract.Deserialize<T>(
            await response.Content.ReadAsStringAsync(cancellationToken), response.StatusCode, ServiceName);
}
