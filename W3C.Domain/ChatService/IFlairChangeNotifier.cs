using System.Collections.Generic;

namespace W3C.Domain.ChatService;

/// <summary>
/// Tells chat-service that one or more players' flair (portrait, chat colour, chat icons, clan) may
/// have changed, so it can re-resolve and push the update to anyone currently viewing them.
/// <para>
/// Fire-and-forget by contract: implementations must never throw and never block the caller. A lost
/// notification degrades to the reconnect backstop — chat-service re-enriches from this service on
/// every connect regardless.
/// </para>
/// </summary>
public interface IFlairChangeNotifier
{
    void NotifyChanged(IReadOnlyCollection<string> battleTags);
}
