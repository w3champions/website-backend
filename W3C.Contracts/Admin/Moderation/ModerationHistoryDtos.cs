using System;
using Newtonsoft.Json;

namespace W3C.Contracts.Admin.Moderation;

/// <summary>
/// Mirrors one row of chat-service's <c>GET /api/moderation/channels?limit=</c> response
/// (the "REST API for the website-backend" section of the chat-service README). Only the
/// fields wb needs are declared; extra wire fields (<c>systemKind</c>, <c>systemRef</c>,
/// <c>lastSeq</c>, <c>lastMessageAt</c>) are simply not modeled here and are ignored by
/// Newtonsoft on deserialization.
/// </summary>
public class ModerationChannelDto()
{
    [JsonProperty("id")]
    public string Id { get; set; }
    [JsonProperty("name")]
    public string Name { get; set; }
    [JsonProperty("type")]
    public ChatChannelType Type { get; set; }
}

/// <summary>
/// Mirrors chat-service's <c>ChannelType</c> enum (<c>W3ChampionsChatService.Domain.ChatEnums</c>).
/// Member ORDER is pinned to chat-service's declaration so the underlying integer values line up.
/// Escalation-1 tolerance note: chat-service's controllers have no <c>JsonStringEnumConverter</c>
/// on this type, so the real wire emits the numeric value (<c>Public=0</c>, etc.) even though the
/// README examples show the string name (<c>"Public"</c>). Newtonsoft's default enum handling
/// natively accepts both an integer token and a matching string token for the same C# enum, so no
/// converter is needed here to be tolerant of either encoding — today's numeric wire or a future
/// chat-side fix to emit strings.
/// </summary>
public enum ChatChannelType
{
    Public,
    SemiPublic,
    System,
    Dm,
    GroupDm
}

/// <summary>
/// Mirrors chat-service's <c>GET /api/moderation/channels/{channelId}/messages?beforeSeq=&amp;limit=</c>
/// 200 response body. The wire also carries a top-level <c>channelId</c>, which wb does not need
/// (the caller already knows which channel it asked for) and is therefore left undeclared.
/// </summary>
public class ModerationMessagePageDto()
{
    [JsonProperty("messages")]
    public ModerationMessageDto[] Messages { get; set; }
    [JsonProperty("nextBeforeSeq")]
    public long? NextBeforeSeq { get; set; }
}

/// <summary>
/// Mirrors one row of the <c>messages</c> array in <see cref="ModerationMessagePageDto"/>.
/// Wire fields with no consumer yet (<c>channelId</c>, <c>seq</c>, <c>senderName</c>,
/// <c>deletedBy</c>, <c>deletedAt</c>) are intentionally left undeclared — Newtonsoft ignores
/// extra JSON properties on deserialization, which is the tolerance mechanism for this lean DTO.
/// </summary>
public class ModerationMessageDto()
{
    [JsonProperty("id")]
    public string Id { get; set; }
    [JsonProperty("content")]
    public string Content { get; set; }
    [JsonProperty("sentAt")]
    public DateTime SentAt { get; set; }
    [JsonProperty("senderBattleTag")]
    public string SenderBattleTag { get; set; }
    [JsonProperty("deleted")]
    public bool Deleted { get; set; }
    [JsonProperty("shadow")]
    public bool Shadow { get; set; }
}
