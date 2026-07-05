using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using W3C.Contracts.Admin.Moderation;
using W3C.Domain.Tracing;

namespace W3C.Domain.ChatService;

public class ChatServiceClient
{
    private static readonly string ChatServiceApiUrl = Environment.GetEnvironmentVariable("CHAT_API") ?? "https://chat-service.test.w3champions.com";
    private readonly JsonSerializerSettings _jsonSerializerSettings;
    private readonly HttpClient _httpClient;

    // Instance (not static) cache: ChatServiceClient is already registered as a DI singleton
    // (Program.cs), so this is process-lifetime state regardless, and keeping it on the instance
    // gives each test a naturally isolated cache. Key: chatRoom.ToLowerInvariant() (Decision 3,
    // case-insensitive room-name lookup). Value: the chat-service channelId resolved for that name.
    private readonly ConcurrentDictionary<string, string> _chatRoomChannelIdCache = new();

    public ChatServiceClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient();

        _jsonSerializerSettings = new JsonSerializerSettings()
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }
        };
    }

    [Trace]
    public async Task<LoungeMuteResponse[]> GetLoungeMutes([NoTrace] string authorization)
    {
        string url = $"{ChatServiceApiUrl}/api/loungeMute";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {authorization}");
        HttpResponseMessage response = await _httpClient.SendAsync(request);

        string content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(content, null, response.StatusCode);
        }
        if (string.IsNullOrEmpty(content)) return null;
        var deserializeObject = JsonConvert.DeserializeObject<LoungeMuteResponse[]>(content);
        return deserializeObject;
    }

    [Trace]
    public async Task<string> PostLoungeMute(LoungeMute loungeMute, [NoTrace] string authorization)
    {
        string url = $"{ChatServiceApiUrl}/api/loungeMute";
        var httpcontent = new StringContent(JsonConvert.SerializeObject(loungeMute), Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Authorization", $"Bearer {authorization}");
        request.Content = httpcontent;
        HttpResponseMessage response = await _httpClient.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(content, null, response.StatusCode);
        }
        return content;
    }

    [Trace]
    public async Task<string> DeleteLoungeMute(string battleTag, [NoTrace] string authorization)
    {
        string encodedTag = HttpUtility.UrlEncode(battleTag);
        string url = $"{ChatServiceApiUrl}/api/loungeMute/{encodedTag}";
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Add("Authorization", $"Bearer {authorization}");
        HttpResponseMessage response = await _httpClient.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(content, null, response.StatusCode);
        }
        return content;
    }

    [NoTrace]
    public async Task<ModerationChannelDto[]> GetModerationChannels(string authorization)
    {
        string url = $"{ChatServiceApiUrl}/api/moderation/channels?limit=500";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {authorization}");
        HttpResponseMessage response = await _httpClient.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(content, null, response.StatusCode);
        }
        var deserializeObject = JsonConvert.DeserializeObject<ModerationChannelDto[]>(content);
        return deserializeObject;
    }

    [NoTrace]
    public async Task<ModerationMessagePageDto> GetModerationChannelMessages(string channelId, string authorization)
    {
        // Defensive encode: channelId is expected to always be a server-resolved value (see
        // GetChatRoomMessages), never raw caller input, but this belt-and-suspenders encode keeps
        // the method safe against path-segment injection even if a future caller gets it wrong.
        string encodedChannelId = HttpUtility.UrlEncode(channelId);
        string url = $"{ChatServiceApiUrl}/api/moderation/channels/{encodedChannelId}/messages?limit=100";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {authorization}");
        HttpResponseMessage response = await _httpClient.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(content, null, response.StatusCode);
        }
        var deserializeObject = JsonConvert.DeserializeObject<ModerationMessagePageDto>(content);
        return deserializeObject;
    }

    /// <summary>
    /// Legacy-parity compatibility shim: resolves the caller-supplied room NAME to a chat-service
    /// channelId (via the cached moderation channels list), fetches the newest message page for
    /// that channelId, and projects it into the frozen <see cref="ChatMessage"/> wire shape.
    /// </summary>
    /// <remarks>
    /// Security note (channelId provenance): <paramref name="chatRoom"/> is only ever used as a
    /// dictionary lookup key against names returned by <see cref="GetModerationChannels"/>. It is
    /// never concatenated into a URL. The only value ever passed to
    /// <see cref="GetModerationChannelMessages"/> is a channelId that chat-service itself returned
    /// in a prior <see cref="GetModerationChannels"/> response -- an unresolved name always short-
    /// circuits to an empty array (Decision 4) before any messages request is made.
    /// </remarks>
    [NoTrace]
    public async Task<ChatMessage[]> GetChatRoomMessages(string chatRoom, [NoTrace] string authorization)
    {
        string normalizedRoom = chatRoom.ToLowerInvariant();

        if (!_chatRoomChannelIdCache.TryGetValue(normalizedRoom, out string channelId))
        {
            channelId = await ResolveChannelId(normalizedRoom, authorization);
            if (channelId == null) return [];
        }

        ModerationMessagePageDto page;
        try
        {
            page = await GetModerationChannelMessages(channelId, authorization);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // The cached channelId went stale (e.g. the channel was recreated server-side). Evict,
            // re-resolve exactly once, and retry exactly once -- bounded so a broken or hostile
            // chat-service can never drive an amplification/retry loop from this client.
            _chatRoomChannelIdCache.TryRemove(normalizedRoom, out _);
            channelId = await ResolveChannelId(normalizedRoom, authorization);
            if (channelId == null) return [];

            try
            {
                page = await GetModerationChannelMessages(channelId, authorization);
            }
            catch (HttpRequestException retryEx) when (retryEx.StatusCode == HttpStatusCode.NotFound)
            {
                return [];
            }
        }

        return [.. page.Messages
            .Where(m => !m.Deleted && !m.Shadow)
            .Select(m => new ChatMessage
            {
                Id = m.Id,
                Message = m.Content,
                Time = m.SentAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                BattleTag = m.SenderBattleTag
            })];
    }

    /// <summary>
    /// Refreshes the name→channelId cache from a fresh <see cref="GetModerationChannels"/> call
    /// (caching every returned Public channel, not just the one being looked up) and returns the
    /// channelId for <paramref name="normalizedRoom"/>, or null if no Public channel matches.
    /// </summary>
    private async Task<string> ResolveChannelId(string normalizedRoom, string authorization)
    {
        ModerationChannelDto[] channels = await GetModerationChannels(authorization);
        foreach (ModerationChannelDto channel in channels)
        {
            if (channel.Type == ChatChannelType.Public)
            {
                _chatRoomChannelIdCache[channel.Name.ToLowerInvariant()] = channel.Id;
            }
        }

        return _chatRoomChannelIdCache.TryGetValue(normalizedRoom, out string channelId) ? channelId : null;
    }
}
