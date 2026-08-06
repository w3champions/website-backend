using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
    // into DeepArchive. Callers turn a null stream into a 404 rather than a 500.
    public async Task<Stream> GenerateReplay(int gameId)
    {
        var response = await _httpClient.GetAsync($"{ReplayServiceUrl}/generate/{gameId}?secret={AdminSecret}", HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) return null;

        var stream = await response.Content.ReadAsStreamAsync();
        var memStream = new MemoryStream();
        await stream.CopyToAsync(memStream);
        memStream.Seek(0, SeekOrigin.Begin);
        return memStream;
    }

    public async Task<ReplayChatsData> GetChatLogs(int gameId)
    {
        var response = await _httpClient.GetAsync($"{ReplayServiceUrl}/chats/{gameId}?secret={AdminSecret}");
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return null;
        var result = JsonConvert.DeserializeObject<ReplayChatsData>(content);
        return result;
    }
}
