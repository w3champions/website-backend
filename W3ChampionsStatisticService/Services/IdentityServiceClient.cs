using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Services;

[Trace]
public class IdentityServiceClient(HttpClient httpClient = null)
{
    private static readonly string IdentityApiUrl = Environment.GetEnvironmentVariable("IDENTIFICATION_SERVICE_URI") ?? "https://identification-service.test.w3champions.com";

    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    // The caller's JWT is forwarded as a per-request Bearer header; it must never appear in the URL.
    private static HttpRequestMessage CreatePermissionsRequest(HttpMethod method, string query, string authorization)
    {
        var request = new HttpRequestMessage(method, $"{IdentityApiUrl}/api/permissions{query}");
        request.Headers.Add("Authorization", $"Bearer {authorization}");
        return request;
    }

    public async Task<List<Permission>> GetPermissions([NoTrace] string authorization)
    {
        using var request = CreatePermissionsRequest(HttpMethod.Get, "", authorization);
        using var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return null;
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException(content, null, response.StatusCode);
        }
        var permissionList = JsonConvert.DeserializeObject<List<Permission>>(content);

        if (!permissionList.Any())
        {
            return new List<Permission>();
        }
        return permissionList;
    }

    public async Task<HttpStatusCode> AddAdmin(Permission permission, [NoTrace] string authorization)
    {
        var serializedObject = JsonConvert.SerializeObject(permission);
        var buffer = System.Text.Encoding.UTF8.GetBytes(serializedObject);
        var byteContent = new ByteArrayContent(buffer);
        byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using var request = CreatePermissionsRequest(HttpMethod.Post, "", authorization);
        request.Content = byteContent;
        using var response = await _httpClient.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(content, null, response.StatusCode);
        }
        return response.StatusCode;
    }

    public async Task<HttpStatusCode> EditAdmin(Permission permission, [NoTrace] string authorization)
    {
        var serializedObject = JsonConvert.SerializeObject(permission);
        var buffer = System.Text.Encoding.UTF8.GetBytes(serializedObject);
        var byteContent = new ByteArrayContent(buffer);
        byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using var request = CreatePermissionsRequest(HttpMethod.Put, "", authorization);
        request.Content = byteContent;
        using var response = await _httpClient.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(content, null, response.StatusCode);
        }
        return response.StatusCode;
    }

    public async Task<HttpStatusCode> DeleteAdmin(string id, [NoTrace] string authorization)
    {
        var encodedTag = HttpUtility.UrlEncode(id);
        using var request = CreatePermissionsRequest(HttpMethod.Delete, $"?id={encodedTag}", authorization);
        using var response = await _httpClient.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(content, null, response.StatusCode);
        }
        return response.StatusCode;
    }

    // UserExists is retired. Both call sites (PlayersController.GetPlayer and
    // PersonalSettingsController.GetPersonalSetting) migrate to ResolveCanonicalBattleTag
    // in this same change. The bool-returning shape is gone; callers explicitly handle
    // the null/canonical/non-canonical cases.

    /// <summary>
    /// Resolves an arbitrary-cased BattleTag to its canonical form by calling
    /// identification-service's /api/users/exists. Returns null if the user doesn't exist.
    /// </summary>
    public virtual async Task<string> ResolveCanonicalBattleTag(string battletag)
    {
        if (battletag == null) return null;
        var encodedTag = HttpUtility.UrlEncode(battletag);
        using var response = await _httpClient.GetAsync($"{IdentityApiUrl}/api/users/exists?id={encodedTag}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"Unexpected status from identification-service: {response.StatusCode}", null, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var body = JsonConvert.DeserializeObject<UserExistsResponse>(content);
        return body?.Id;
    }

    private class UserExistsResponse
    {
        public string Id { get; set; }
    }
}
