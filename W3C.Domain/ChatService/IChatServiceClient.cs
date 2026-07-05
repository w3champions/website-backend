using System.Threading.Tasks;
using W3C.Contracts.Admin.Moderation;
using W3C.Domain.Tracing;

namespace W3C.Domain.ChatService;

/// <summary>
/// Public contract for <see cref="ChatServiceClient"/>. Only covers the methods consumed outside
/// the domain project (currently <c>ModerationController</c>). The moderation-history endpoint
/// methods (<c>GetModerationChannels</c>, <c>GetModerationChannelMessages</c>) are internal
/// implementation details of <see cref="ChatServiceClient.GetChatRoomMessages"/> and intentionally
/// stay off this interface.
/// </summary>
public interface IChatServiceClient
{
    [Trace]
    Task<LoungeMuteResponse[]> GetLoungeMutes([NoTrace] string authorization);

    [Trace]
    Task<string> PostLoungeMute(LoungeMute loungeMute, [NoTrace] string authorization);

    [Trace]
    Task<string> DeleteLoungeMute(string battleTag, [NoTrace] string authorization);

    [NoTrace]
    Task<ChatMessage[]> GetChatRoomMessages(string chatRoom, [NoTrace] string authorization);
}
