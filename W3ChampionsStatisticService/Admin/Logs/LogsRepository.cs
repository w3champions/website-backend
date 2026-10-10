using W3C.Domain;
using MongoDB.Driver;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using System.IO;
using W3C.Domain.Tracing;
namespace W3ChampionsStatisticService.Admin.Logs;

[Trace]
public class LogsRepository(MongoClient mongoClient, IHttpClientFactory httpClientFactory) : MongoDbRepositoryBase(mongoClient), ILogsRepository
{
    private static readonly string MatchmakingApiUrl = Environment.GetEnvironmentVariable("MATCHMAKING_API") ?? "https://matchmaking-service.test.w3champions.com";

    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(HttpClientNames.MatchmakingLogs);

    // The admin secret goes on each request, never on the shared client's DefaultRequestHeaders.
    private async Task<HttpResponseMessage> GetAdminAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AdminSecretProvider.AddTo(request.Headers);
        return await _httpClient.SendAsync(request);
    }

    public async Task<List<string>> GetLogfileNames()
    {
        var url = $"{MatchmakingApiUrl}/admin/logs";
        var response = await GetAdminAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return new List<string> { "Unable to get logfiles." };
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException(content, null, response.StatusCode);
        }
        var logfileNames = JsonConvert.DeserializeObject<List<string>>(content);
        return logfileNames;
    }

    public async Task<List<string>> GetLogContent(string logfileName)
    {
        var url = $"{MatchmakingApiUrl}/admin/logs/{logfileName}";
        var response = await GetAdminAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return new List<string> { "Unable to get log content." };
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException(content, null, response.StatusCode);
        }
        var logfileContent = JsonConvert.DeserializeObject<List<string>>(content);
        return logfileContent;
    }

    public async Task<Stream> DownloadLog(string logfileName)
    {
        var url = $"{MatchmakingApiUrl}/admin/logs/download/{logfileName}";
        var response = await GetAdminAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException(content, null, response.StatusCode);
        }
        Stream stream = await response.Content.ReadAsStreamAsync();
        return stream;
    }
}
