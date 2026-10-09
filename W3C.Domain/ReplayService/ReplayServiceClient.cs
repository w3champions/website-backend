using System;
using System.IO;
using System.Net;
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
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient();

    // The admin secret is sent as a per-request header; it must never appear in the URL.
    private static HttpRequestMessage CreateRequest(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{ReplayServiceUrl}{path}");
        AdminSecretProvider.AddTo(request.Headers);
        return request;
    }

    public async Task<Stream> GenerateReplay(int gameId)
    {
        // Mirrors GetStreamAsync: stream the body and throw on a non-success status. The body
        // is copied into a MemoryStream, so the response can be disposed before returning.
        using var request = CreateRequest($"/generate/{gameId}");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) ThrowForFailure(response, $"the replay of game {gameId}");
        await using var stream = await response.Content.ReadAsStreamAsync();
        var memStream = new MemoryStream();
        await stream.CopyToAsync(memStream);
        memStream.Seek(0, SeekOrigin.Begin);
        return memStream;
    }

    public async Task<ReplayChatsData> GetChatLogs(int gameId)
    {
        using var request = CreateRequest($"/chats/{gameId}");
        using var response = await _httpClient.SendAsync(request);

        // replay-service answers 200 with empty collections for a game without chats, and 404/410 when the
        // game or its replay is absent or archived. Like GenerateReplay, any failure surfaces as an
        // HttpRequestException carrying the status, so callers get 404/410 instead of an all-null body.
        if (!response.IsSuccessStatusCode) ThrowForFailure(response, $"the chat logs of game {gameId}");

        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(content)) return null;
        var result = JsonConvert.DeserializeObject<ReplayChatsData>(content);
        return result;
    }

    // Only 404 (not found) and 410 (archived) are meaningful to the browser. Every other upstream failure
    // (including a rejected admin secret, a 400/429 or a 5xx) is replay-service's problem, not the caller's,
    // so it must not reach the browser as if website-backend had produced it: it surfaces as 502 Bad Gateway.
    // The upstream status stays in the message; the upstream body is never included.
    private static void ThrowForFailure(HttpResponseMessage response, string what)
    {
        var surfacedStatus = response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone
            ? response.StatusCode
            : HttpStatusCode.BadGateway;
        throw new HttpRequestException(
            $"Replay service returned {(int)response.StatusCode} ({response.StatusCode}) for {what}.",
            null,
            surfacedStatus);
    }
}
