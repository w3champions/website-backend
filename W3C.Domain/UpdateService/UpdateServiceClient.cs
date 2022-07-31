using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using W3C.Domain.UpdateService.Contracts;

namespace W3C.Domain.UpdateService
{
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
            var result = await _httpClient.GetAsync($"{UpdateServiceUrl}/api/content/maps?secret={AdminSecret}&mapId={mapId}");
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<MapFileData[]>(content);
            return deserializeObject;
        }

        public async Task<MapFileData> CreateMapFromFormAsync(HttpRequestMessage request)
        {
            var response = await _httpClient.PostAsync($"{UpdateServiceUrl}/api/content/maps?secret={AdminSecret}", request.Content);
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var result = JsonConvert.DeserializeObject<MapFileData>(content);
            return result;
        }

        public async Task<MapFileData> GetMapFile(string fileId)
        {
            var result = await _httpClient.GetAsync($"{UpdateServiceUrl}/api/content/maps/{fileId}?secret={AdminSecret}");
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<MapFileData>(content);
            return deserializeObject;
        }

        public async Task DeleteMapFile(string fileId)
        {
            var result = await _httpClient.DeleteAsync($"{UpdateServiceUrl}/api/content/maps/{fileId}?secret={AdminSecret}");
            if (!result.IsSuccessStatusCode)
            {
                throw new Exception($"Unable to delete map file with id {fileId}");
            }
        }
    }
}
