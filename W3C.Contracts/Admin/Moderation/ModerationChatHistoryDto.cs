namespace W3C.Contracts.Admin.Moderation;

/// <summary>
/// Response shape for the paged moderation chat-history endpoint
/// (<c>GET /api/moderation/launcher-chat/{chatRoom}/messages</c>). Unlike the legacy
/// <see cref="ChatMessage"/> shape, this carries every row chat-service returns -- including
/// deleted/shadow rows, flagged via <see cref="ModerationChatMessageDto.Deleted"/> and
/// <see cref="ModerationChatMessageDto.Shadow"/> -- plus the cursor for paging further back.
/// Bare POCO (no Newtonsoft attributes) to match <see cref="ChatMessage"/>'s precedent: this DTO
/// is only ever serialized by ASP.NET Core's System.Text.Json controller pipeline, which
/// camelCases property names by default (see <c>JsonSerializerDefaults.Web</c>).
/// </summary>
public class ModerationChatHistoryDto()
{
    public ModerationChatMessageDto[] Messages { get; set; }
    public long? NextBeforeSeq { get; set; }
}

/// <summary>
/// One moderation chat message row, flags included. <see cref="Time"/> and
/// <see cref="DeletedAt"/> are rendered as ISO-8601 ("o") UTC strings; <see cref="DeletedAt"/> is
/// null when <see cref="Deleted"/> is false.
/// </summary>
public class ModerationChatMessageDto()
{
    public string Id { get; set; }
    public long Seq { get; set; }
    public string Message { get; set; }
    public string Time { get; set; }
    public string BattleTag { get; set; }
    public string SenderName { get; set; }
    public bool Deleted { get; set; }
    public string DeletedBy { get; set; }
    public string DeletedAt { get; set; }
    public bool Shadow { get; set; }
}
