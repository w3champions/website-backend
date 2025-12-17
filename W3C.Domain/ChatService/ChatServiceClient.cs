using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Linq;
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
    public async Task<ChatMessage[]> GetChatRoomMessages(string chatRoom, string authorization)
    {
        string url = $"{ChatServiceApiUrl}/api/chat/{chatRoom}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {authorization}");
        HttpResponseMessage response = await _httpClient.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(content, null, response.StatusCode);
        }
        var deserializeObject = JsonConvert.DeserializeObject<ChatMessageDto[]>(content);

        ChatMessage[] chatmessages = [.. deserializeObject.Select(d => new ChatMessage
        {
            Id = d.Id,
            Message = d.Message,
            Time = d.Time,
            BattleTag = d.User.BattleTag
        })];

        return chatmessages;
    }
}
