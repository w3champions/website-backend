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

    public async Task<List<Permission>> GetPermissions([NoTrace] string authorization)
    {
        var response = await _httpClient.GetAsync($"{IdentityApiUrl}/api/permissions?authorization={authorization}");
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
        var response = await _httpClient.PostAsync($"{IdentityApiUrl}/api/permissions?authorization={authorization}", byteContent);
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
        var response = await _httpClient.PutAsync($"{IdentityApiUrl}/api/permissions?authorization={authorization}", byteContent);
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
        var response = await _httpClient.DeleteAsync($"{IdentityApiUrl}/api/permissions?id={encodedTag}&authorization={authorization}");
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
        var response = await _httpClient.GetAsync($"{IdentityApiUrl}/api/users/exists?id={encodedTag}");
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
