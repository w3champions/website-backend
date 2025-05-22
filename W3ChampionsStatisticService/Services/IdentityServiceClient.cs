﻿using System;
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
public class IdentityServiceClient()
{
    private static readonly string IdentityApiUrl = Environment.GetEnvironmentVariable("IDENTIFICATION_SERVICE_URI") ?? "https://identification-service.test.w3champions.com";

    public async Task<List<Permission>> GetPermissions([NoTrace] string authorization)
    {
        var httpClient = new HttpClient();
        var response = await httpClient.GetAsync($"{IdentityApiUrl}/api/permissions?authorization={authorization}");
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
        var httpClient = new HttpClient();
        var serializedObject = JsonConvert.SerializeObject(permission);
        var buffer = System.Text.Encoding.UTF8.GetBytes(serializedObject);
        var byteContent = new ByteArrayContent(buffer);
        byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var response = await httpClient.PostAsync($"{IdentityApiUrl}/api/permissions?authorization={authorization}", byteContent);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(content, null, response.StatusCode);
        }
        return response.StatusCode;
    }

    public async Task<HttpStatusCode> EditAdmin(Permission permission, [NoTrace] string authorization)
    {
        var httpClient = new HttpClient();
        var serializedObject = JsonConvert.SerializeObject(permission);
        var buffer = System.Text.Encoding.UTF8.GetBytes(serializedObject);
        var byteContent = new ByteArrayContent(buffer);
        byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var response = await httpClient.PutAsync($"{IdentityApiUrl}/api/permissions?authorization={authorization}", byteContent);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(content, null, response.StatusCode);
        }
        return response.StatusCode;
    }

    public async Task<HttpStatusCode> DeleteAdmin(string id, [NoTrace] string authorization)
    {
        var httpClient = new HttpClient();
        var encodedTag = HttpUtility.UrlEncode(id);
        var response = await httpClient.DeleteAsync($"{IdentityApiUrl}/api/permissions?id={encodedTag}&authorization={authorization}");
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(content, null, response.StatusCode);
        }
        return response.StatusCode;
    }

    public async Task<bool> UserExists(string battletag)
    {
        var httpClient = new HttpClient();
        var encodedTag = HttpUtility.UrlEncode(battletag);
        string url = $"{IdentityApiUrl}/api/users/exists?id={encodedTag}";
        var response = await httpClient.GetAsync(url);
        return response.StatusCode == HttpStatusCode.OK;
    }
}
