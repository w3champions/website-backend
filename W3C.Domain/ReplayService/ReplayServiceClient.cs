using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;
using W3C.Contracts.Replay;
using W3C.Domain.Tracing;

namespace W3C.Domain.UpdateService;

[Trace]
public class ReplayServiceClient(IHttpClientFactory httpClientFactory)
{
    private static readonly string ReplayServiceUrl = Environment.GetEnvironmentVariable("REPLAY_API") ?? "https://replay-service.test.w3champions.com";
    private static readonly string AdminSecret = Environment.GetEnvironmentVariable("ADMIN_SECRET") ?? "300C018C-6321-4BAB-B289-9CB3DB760CBB";
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient();

    // A replay legitimately may not exist: it was never recorded, or it has aged
    // into DeepArchive (NotFound/Gone). Callers turn a null stream into a 404 rather
    // than a 500. Any other non-success status is a genuine upstream failure (outage,
    // timeout, etc.) and must not be mistaken for "no replay" -- it is logged and thrown
    // so it surfaces as a server error instead of a misleading 404.
    public async Task<Stream> GenerateReplay(int gameId)
    {
        using var response = await _httpClient.GetAsync($"{ReplayServiceUrl}/generate/{gameId}?secret={AdminSecret}", HttpCompletionOption.ResponseHeadersRead);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Log.Error("Replay service returned {StatusCode} generating replay for game {GameId}: {Content}", response.StatusCode, gameId, errorContent);
            throw new HttpRequestException(errorContent, null, response.StatusCode);
        }

        var stream = await response.Content.ReadAsStreamAsync();
        var memStream = new MemoryStream();
        await stream.CopyToAsync(memStream);
        memStream.Seek(0, SeekOrigin.Begin);
        return memStream;
    }

    public async Task<ReplayChatsData> GetChatLogs(int gameId)
    {
        using var response = await _httpClient.GetAsync($"{ReplayServiceUrl}/chats/{gameId}?secret={AdminSecret}");

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Log.Error("Replay service returned {StatusCode} fetching chat logs for game {GameId}: {Content}", response.StatusCode, gameId, errorContent);
            throw new HttpRequestException(errorContent, null, response.StatusCode);
        }

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return null;
        var result = JsonConvert.DeserializeObject<ReplayChatsData>(content);
        return result;
    }
}
