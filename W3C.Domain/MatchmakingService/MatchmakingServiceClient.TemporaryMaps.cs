using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService.Contracts;
using W3C.Domain.Tracing;

namespace W3C.Domain.MatchmakingService;

/// <summary>
/// The x-admin-secret routes matchmaking-service exposes for self-provided (temporary) maps — design spec
/// §5.4 / Appendix A.5. Consumed only by website-backend; never log a mapProof or a proofHash (§10.3).
/// </summary>
public partial class MatchmakingServiceClient
{
    private static readonly char[] JsonWhitespace = [' ', '\t', '\r', '\n'];

    // ---- Temporary (self-provided) maps -------------------------------------------------
    // All routes carry x-admin-secret and are consumed only by website-backend. 404 and 409 are
    // EXPECTED outcomes here, not errors, so these methods branch on the status code themselves.
    // Never log a mapProof or a proofHash (design spec §10.3).

    /// <summary>Upload dedupe probe. Returns null when no temporary map has this sha1 (strict 404 {}).</summary>
    public async Task<MapContract> GetTemporaryMapBySha1(string sha1)
    {
        var url = $"{MatchmakingApiUrl}/maps/temporary/by-sha1/{Uri.EscapeDataString(sha1)}";
        var envelope = await ReadRecordOrNull<TemporaryMapEnvelope>(await SendWithSecret(HttpMethod.Get, url));
        return envelope == null ? null : RequireRecord(envelope.Map);
    }

    /// <summary>Pre-check probe. Returns null when nothing is known for this proofHash (strict 404 {}).</summary>
    public async Task<TemporaryMapStateResponse> GetTemporaryMapStateByProofHash([NoTrace] string proofHash)
    {
        var url = $"{MatchmakingApiUrl}/maps/temporary/by-proof-hash/{Uri.EscapeDataString(proofHash)}";
        var state = await ReadRecordOrNull<TemporaryMapStateResponse>(await SendWithSecret(HttpMethod.Get, url));
        return state == null ? null : RequireFileState(state, state.FileState);
    }

    /// <summary>Reconciliation probe: does a record still claim this stored file? Null means orphan (strict 404 {}).</summary>
    public async Task<MapContract> GetTemporaryMapByPath(string path)
    {
        var url = $"{MatchmakingApiUrl}/maps/temporary/by-path?path={HttpUtility.UrlEncode(path)}";
        var envelope = await ReadRecordOrNull<TemporaryMapEnvelope>(await SendWithSecret(HttpMethod.Get, url));
        return envelope == null ? null : RequireRecord(envelope.Map);
    }

    public async Task<CreateTemporaryMapResult> CreateTemporaryMap([NoTrace] CreateTemporaryMapRequest request)
    {
        var url = $"{MatchmakingApiUrl}/maps/temporary";
        var response = await SendWithSecret(HttpMethod.Post, url, SerializeData(request));

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var conflicting = await GetResult<TemporaryMapEnvelope>(response);
            return new CreateTemporaryMapResult { Created = false, Map = RequireRecord(conflicting?.Map) };
        }

        if (!response.IsSuccessStatusCode) await HandleMMError(response);
        var created = await GetResult<TemporaryMapEnvelope>(response);
        return new CreateTemporaryMapResult { Created = true, Map = RequireRecord(created?.Map) };
    }

    /// <summary>Non-mutating proof lookup. Returns null when no record has this proofHash (strict 404 {}).</summary>
    public async Task<VerifyTemporaryMapProofResponse> VerifyTemporaryMapProof([NoTrace] string proofHash)
    {
        var url = $"{MatchmakingApiUrl}/maps/temporary/verify-proof";
        var response = await SendWithSecret(HttpMethod.Post, url, SerializeData(new { proofHash }));
        var verified = await ReadRecordOrNull<VerifyTemporaryMapProofResponse>(response);
        return verified == null ? null : RequireFileState(verified, verified.FileState);
    }

    /// <summary>Call ONLY after the bytes are stored in update-service: this flips fileState to present.</summary>
    public async Task<MapContract> MarkTemporaryMapFileRestored(int mapId, [NoTrace] TemporaryMapFileRestoredRequest request)
    {
        var url = $"{MatchmakingApiUrl}/maps/temporary/{mapId.ToString(CultureInfo.InvariantCulture)}/file-restored";
        var response = await SendWithSecret(HttpMethod.Post, url, SerializeData(request));
        if (!response.IsSuccessStatusCode) await HandleMMError(response);
        return RequireRecord((await GetResult<TemporaryMapEnvelope>(response))?.Map);
    }

    public async Task<MapContract> MarkTemporaryMapFileDeleted(int mapId)
    {
        var url = $"{MatchmakingApiUrl}/maps/temporary/{mapId.ToString(CultureInfo.InvariantCulture)}/file-deleted";
        var response = await SendWithSecret(HttpMethod.Post, url);
        if (!response.IsSuccessStatusCode) await HandleMMError(response);
        return RequireRecord((await GetResult<TemporaryMapEnvelope>(response))?.Map);
    }

    public async Task<ExpiredTemporaryMapsResponse> GetExpiredTemporaryMaps(long beforeEpochMs, int limit)
    {
        var url = $"{MatchmakingApiUrl}/maps/temporary/expired" +
                  $"?before={beforeEpochMs.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}";
        var response = await SendWithSecret(HttpMethod.Get, url);
        if (!response.IsSuccessStatusCode) await HandleMMError(response);
        var expired = await GetResult<ExpiredTemporaryMapsResponse>(response);
        return expired?.Items == null ? new ExpiredTemporaryMapsResponse() : expired;
    }

    /// <summary>
    /// Spec §5.4 pins <c>404 {}</c> as the record-not-found answer of every null-returning probe. Any
    /// other 404 (an HTML "Cannot GET" page, a proxy page, a non-empty body) means the ROUTE was not found,
    /// and reading it as "no record" would create duplicate records or delete claimed files — so it throws.
    /// </summary>
    private async Task<T> ReadRecordOrNull<T>(HttpResponseMessage response)
        where T : class
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (IsEmptyJsonObject(body)) return null;
            throw new HttpRequestException(
                "matchmaking-service answered 404 without the empty-object record-not-found body", null, HttpStatusCode.NotFound);
        }

        if (!response.IsSuccessStatusCode) await HandleMMError(response);
        return await GetResult<T>(response);
    }

    private static bool IsEmptyJsonObject(string body)
    {
        var trimmed = (body ?? "").AsSpan().Trim(JsonWhitespace);
        return trimmed.Length >= 2
               && trimmed[0] == '{'
               && trimmed[^1] == '}'
               && trimmed[1..^1].Trim(JsonWhitespace).IsEmpty;
    }

    /// <summary>
    /// A success status without the record Appendix A.5 pins is an upstream fault, never "no record":
    /// only a strict 404 {} may mean that.
    /// </summary>
    private static T RequireRecord<T>(T record)
        where T : class
        => record ?? throw MissingRecord();

    /// <summary>As <see cref="RequireRecord{T}(T)"/>, for the status-bearing responses: a record without a fileState is no answer.</summary>
    private static T RequireFileState<T>(T record, string fileState)
        where T : class
        => string.IsNullOrEmpty(fileState) ? throw MissingRecord() : record;

    private static HttpRequestException MissingRecord()
        => new("matchmaking-service answered without the temporary-map record", null, HttpStatusCode.BadGateway);

    private Task<HttpResponseMessage> SendWithSecret(HttpMethod method, string url, string jsonBody = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("x-admin-secret", AdminSecret);
        if (jsonBody != null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return _httpClient.SendAsync(request);
    }
}
