using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using W3C.Contracts.Admin.Moderation;

namespace W3C.Domain.ChatService
{
    public class ChatServiceClient
    {
        private static readonly string ChatServiceApiUrl = Environment.GetEnvironmentVariable("CHAT_API") ?? "https://chat-service.test.w3champions.com";
        private static readonly string AdminSecret = Environment.GetEnvironmentVariable("ADMIN_SECRET") ?? "300C018C-6321-4BAB-B289-9CB3DB760CBB";
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

        public async Task<LoungeMuteResponse[]> GetLoungeMutes(string authorization)
        {
            var result = await _httpClient.GetAsync($"{ChatServiceApiUrl}/api/loungeMute/?authorization={authorization}");
            var content = await result.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return null;
            var deserializeObject = JsonConvert.DeserializeObject<LoungeMuteResponse[]>(content);
            return deserializeObject;
        }

        public async Task<HttpResponseMessage> PostLoungeMute(LoungeMute loungeMute)
        {
            var httpcontent = new StringContent(JsonConvert.SerializeObject(loungeMute), Encoding.UTF8, "application/json");
            var result = await _httpClient.PostAsync($"{ChatServiceApiUrl}/api/loungeMute/?secret={AdminSecret}", httpcontent);
            return result;
        }

        public async Task<HttpResponseMessage> DeleteLoungeMute(string battleTag)
        {
            var encodedTag = HttpUtility.UrlEncode(battleTag);
            var result = await _httpClient.DeleteAsync($"{ChatServiceApiUrl}/api/loungeMute/{encodedTag}?secret={AdminSecret}");
            return result;
        }
    }
}
