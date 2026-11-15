using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Net.Http.Headers;
using Serilog;

namespace W3ChampionsStatisticService.Admin;

/// <summary>
/// Shared plumbing of the matchmaking smurf-detection admin proxies (hardware and events): the path prefix, segment
/// encoding, the admin secret, the 403 and network-failure mapping to a generic 502.
/// </summary>
public partial class AdminRepository
{
    private const string SmurfDetectionPath = "/admin/smurf-detection";

    // BattleTags contain '#', so every segment is re-encoded for matchmaking.
    private static string Segment(string value) => Uri.EscapeDataString(value);

    // The raw value is forwarded unchanged, but only when the typed parse accepts it (delta-seconds or HTTP date).
    private static string WellFormedRetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter != null && response.Headers.NonValidated.TryGetValues(HeaderNames.RetryAfter, out var values)
            ? values.ToString()
            : null;

    private async Task<MatchmakingProxyResult> SendSmurfDetection(HttpMethod method, string path, object body = null) =>
        await SendProxy(method, MatchmakingApiUrl + SmurfDetectionPath + path,
            body == null ? null : CreateJsonContent(body), forward403: null);

    /// <summary>
    /// Sends one admin request to matchmaking and maps the outcome to a <see cref="MatchmakingProxyResult"/>. A 403 is returned
    /// as is only when <paramref name="forward403"/> accepts its body; otherwise it (a refused admin secret) and any network
    /// failure become the generic 502.
    /// </summary>
    private async Task<MatchmakingProxyResult> SendProxy(HttpMethod method, string url, HttpContent content, Func<string, bool> forward403)
    {
        using var request = CreateAdminRequest(method, url);
        if (content != null) request.Content = content;
        try
        {
            using var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.Forbidden && forward403?.Invoke(responseBody) != true)
            {
                Log.Error("Matchmaking rejected the admin credentials for an admin proxy {Method} request", method.Method);
                return MatchmakingProxyResult.Unavailable();
            }
            return new MatchmakingProxyResult((int)response.StatusCode, responseBody, WellFormedRetryAfter(response));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The message can carry the upstream address, so only the exception type is logged.
            Log.Error("Matchmaking admin proxy request {Method} failed: {ExceptionType}", method.Method, ex.GetType().Name);
            return MatchmakingProxyResult.Unavailable();
        }
    }
}
