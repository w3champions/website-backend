using Newtonsoft.Json;

namespace W3C.Contracts.Admin.Moderation;

public class ChatMessageDto()
{
    [JsonProperty("id")]
    public string Id { get; set; }
    [JsonProperty("message")]
    public string Message { get; set; }
    [JsonProperty("time")]
    public string Time { get; set; }
    [JsonProperty("user")]
    public User User { get; set; }
}

public class User()
{
    public string BattleTag { get; set; }
}
