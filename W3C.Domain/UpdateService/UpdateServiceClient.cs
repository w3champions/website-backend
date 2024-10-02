using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using W3C.Domain.UpdateService.Contracts;

namespace W3C.Domain.UpdateService;

public class UpdateServiceClient
{
    private static readonly string UpdateServiceUrl = Environment.GetEnvironmentVariable("UPDATE_API") ?? "https://update-service.test.w3champions.com";
    private static readonly string AdminSecret = Environment.GetEnvironmentVariable("ADMIN_SECRET") ?? "300C018C-6321-4BAB-B289-9CB3DB760CBB";

    private readonly HttpClient _httpClient;
    public UpdateServiceClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<MapFileData[]> GetMapFiles(int mapId)
    {
        var url = $"{UpdateServiceUrl}/api/content/maps?mapId={mapId}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await _httpClient.SendAsync(request);

        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var errMessage = JsonConvert.DeserializeObject<ErrorData>(content);
            throw new HttpRequestException(errMessage.message, null, response.StatusCode);
        }
        if (string.IsNullOrEmpty(content)) throw new HttpRequestException("Unable to get map files!", null, HttpStatusCode.ServiceUnavailable);

        var deserializeObject = JsonConvert.DeserializeObject<MapFileData[]>(content);
        return deserializeObject;
    }

    public async Task<MapFileData> CreateMapFromFormAsync(HttpRequestMessage req)
    {
        var url = $"{UpdateServiceUrl}/api/content/maps";
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-admin-secret", AdminSecret);
        request.Content = req.Content;
        var response = await _httpClient.SendAsync(request);

        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var errMessage = JsonConvert.DeserializeObject<ErrorData>(content);
            throw new HttpRequestException(errMessage.message, null, response.StatusCode);
        }
        if (string.IsNullOrEmpty(content)) throw new HttpRequestException("Map creation failed!", null, HttpStatusCode.ServiceUnavailable);

        var deserializeObject = JsonConvert.DeserializeObject<MapFileData>(content);
        return deserializeObject;
    }

    public async Task<MapFileData> GetMapFile(string fileId)
    {
        var url = $"{UpdateServiceUrl}/api/content/maps/{fileId}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await _httpClient.SendAsync(request);

        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var errMessage = JsonConvert.DeserializeObject<ErrorData>(content);
            throw new HttpRequestException(errMessage.message, null, response.StatusCode);
        }
        if (string.IsNullOrEmpty(content)) throw new HttpRequestException("Unable to get map file!", null, HttpStatusCode.ServiceUnavailable);
        var deserializeObject = JsonConvert.DeserializeObject<MapFileData>(content);
        return deserializeObject;
    }

    public async Task DeleteMapFile(string fileId)
    {
        var url = $"{UpdateServiceUrl}/api/content/maps/{fileId}";
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Add("x-admin-secret", AdminSecret);
        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Unable to delete map file with id {fileId}", null, response.StatusCode);
        }
    }
}
