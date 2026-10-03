using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Serilog;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Common;
using W3C.Domain.MatchmakingService.Contracts;

namespace W3C.Domain.MatchmakingService;

/// <summary>
/// The map-pool routes: the two listings the website relays as lists and the admin create/update writes. Every
/// request that carries x-admin-secret goes through <see cref="SendWithSecret"/>, which the temporary-map routes
/// share. <c>GetMap(int)</c> stays in the main file with the other untouched passthroughs.
/// </summary>
public partial class MatchmakingServiceClient
{
    public async Task<GetMapsResponse> GetMaps(GetMapsRequest request)
    {
        List<string> queryParams = [];

        if (!string.IsNullOrEmpty(request.Filter))
        {
            queryParams.Add($"filter={HttpUtility.UrlEncode(request.Filter)}");
        }

        // Admin-only by the caller's [BearerHasPermissionFilter]; matchmaking additionally honours it
        // only for admin-secret callers and strips mapProof/proofHash from every row.
        if (request.IncludeTemporary)
        {
            queryParams.Add("includeTemporary=true");
        }

        var url = $"{MatchmakingApiUrl}/maps?{string.Join("&", queryParams)}";

        // The admin secret is LOAD-BEARING here, not decoration. matchmaking gates includeTemporary on
        // isAdminRequest(req) -> presentedSecrets(req).some(matchesSecret), which is false when no
        // secret is presented AND false when none is configured — it never falls open. Without this
        // header mm answers 200 with the permanent-only list, so the website's "Show temporary maps"
        // checkbox becomes a silent permanent no-op: nothing fails and nothing logs. Sending it on a
        // permanent-only listing is behaviour-neutral.
        var response = await SendWithSecret(HttpMethod.Get, url);
        return await ReadMapListing(response, nameof(GetMaps));
    }

    public async Task<GetMapsResponse> GetTournamentMaps()
    {
        var url = $"{MatchmakingApiUrl}/maps/tournaments";
        var response = await _httpClient.GetAsync(url);
        return await ReadMapListing(response, nameof(GetTournamentMaps));
    }

    public async Task<MapContract> CreateMap(MapContract newMap)
    {
        var url = $"{MatchmakingApiUrl}/maps";
        var response = await SendWithSecret(HttpMethod.Post, url, SerializeData(AdminMapWriteRequest.From(newMap)));

        if (response.IsSuccessStatusCode)
        {
            return await GetResult<MapContract>(response);
        }

        await HandleMMError(response);
        return null;
    }

    public async Task<MapContract> UpdateMap(int id, MapContract map)
    {
        var url = $"{MatchmakingApiUrl}/maps/{id}";
        var response = await SendWithSecret(HttpMethod.Put, url, SerializeData(AdminMapWriteRequest.From(map)));

        if (response.IsSuccessStatusCode)
        {
            return await GetResult<MapContract>(response);
        }

        await HandleMMError(response);
        return null;
    }

    /// <summary>
    /// Both map listings are relayed to callers as a list, so a failure must never read as one. An error status throws
    /// with that status, and a success whose body is not a listing, or whose listing holds a null row (no map, yet it
    /// would be re-served as one), throws a contract violation (see UpstreamContract). Neither message quotes the body
    /// or the URL. A 401 or 403 from matchmaking means website-backend's own
    /// admin-secret configuration is wrong, and a 407 that a proxy on the way demands credentials: never the caller's
    /// authentication, so each surfaces as 502 (the message still names the upstream status), because relayed as-is
    /// the website would treat it as the caller's own auth failure. The global filter then logs only the 502 it
    /// answers, so that case is logged here: service, listing and upstream status only, never the URL or the body.
    /// </summary>
    private static async Task<GetMapsResponse> ReadMapListing(HttpResponseMessage response, string listing)
    {
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.ProxyAuthenticationRequired)
            {
                Log.Warning("{Service} answered {UpstreamStatusCode} to {Listing}, answered as 502: website-backend's own credential or proxy configuration, not the caller's",
                    ServiceName, (int)status, listing);
                status = HttpStatusCode.BadGateway;
            }

            throw new HttpRequestException($"{ServiceName} answered {(int)response.StatusCode}", null, status);
        }

        var maps = UpstreamContract.Deserialize<GetMapsResponse>(
            await response.Content.ReadAsStringAsync(), response.StatusCode, ServiceName);
        return Array.TrueForAll(maps.Items, row => row is not null) ? maps : throw UpstreamContract.Violation(response.StatusCode, ServiceName);
    }

    /// <summary>Sends a request carrying x-admin-secret and, when given, a JSON body.</summary>
    private Task<HttpResponseMessage> SendWithSecret(
        HttpMethod method, string url, string jsonBody = null, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("x-admin-secret", AdminSecret);
        if (jsonBody != null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return _httpClient.SendAsync(request, cancellationToken);
    }
}
