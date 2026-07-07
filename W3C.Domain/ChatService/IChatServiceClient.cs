using System.Threading.Tasks;
using W3C.Contracts.Admin.Moderation;
using W3C.Domain.Tracing;

namespace W3C.Domain.ChatService;

/// <summary>
/// Public contract for <see cref="ChatServiceClient"/>. Only covers the methods consumed outside
/// the domain project (currently <c>ModerationController</c>). <c>GetModerationChannels</c> and
/// <c>GetModerationChannelMessages</c> are internal implementation details of
/// <see cref="ChatServiceClient.GetChatRoomMessages"/> and <see cref="GetModerationChannelHistory"/>
/// and intentionally stay off this interface.
/// </summary>
public interface IChatServiceClient
{
    [Trace]
    Task<LoungeMuteResponse[]> GetLoungeMutes([NoTrace] string authorization);

    [Trace]
    Task<string> PostLoungeMute(LoungeMute loungeMute, [NoTrace] string authorization);

    [Trace]
    Task<string> DeleteLoungeMute(string battleTag, [NoTrace] string authorization);

    /// <summary>
    /// Legacy-parity shim: newest 100-message page only, deleted/shadow rows filtered out. Kept
    /// for the existing admin page's wire shape (<see cref="ChatMessage"/>); prefer
    /// <see cref="GetModerationChannelHistory"/> for new consumers that need paging or flags.
    /// </summary>
    [NoTrace]
    Task<ChatMessage[]> GetChatRoomMessages(string chatRoom, [NoTrace] string authorization);

    /// <summary>
    /// Cursor-paged moderation chat history for <paramref name="chatRoom"/>, including deleted and
    /// shadow rows (flagged, not hidden). Pass the previous page's <c>NextBeforeSeq</c> as
    /// <paramref name="beforeSeq"/> to page further back; null fetches the newest page.
    /// <paramref name="limit"/> is clamped to [1, 100], defaulting to 100 when null. Returns an
    /// empty page (never null) when <paramref name="chatRoom"/> cannot be resolved.
    /// </summary>
    [NoTrace]
    Task<ModerationChatHistoryDto> GetModerationChannelHistory(string chatRoom, long? beforeSeq, int? limit, [NoTrace] string authorization);
}
