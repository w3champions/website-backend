using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
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

    private async Task<MatchmakingProxyResult> SendSmurfDetection(HttpMethod method, string path, object body = null)
    {
        using var request = CreateAdminRequest(method, MatchmakingApiUrl + SmurfDetectionPath + path);
        if (body != null) request.Content = CreateJsonContent(body);
        try
        {
            using var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                Log.Error("Matchmaking rejected the admin credentials for a smurf moderation {Method} request", method.Method);
                return MatchmakingProxyResult.Unavailable();
            }
            return new MatchmakingProxyResult((int)response.StatusCode, content);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The message can carry the upstream address, so only the exception type is logged.
            Log.Error("Matchmaking smurf moderation request {Method} failed: {ExceptionType}", method.Method, ex.GetType().Name);
            return MatchmakingProxyResult.Unavailable();
        }
    }
}
