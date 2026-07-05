namespace W3C.Domain.ChatService;

/// <summary>
/// Fire-and-forget notifier that pings chat-service about a player-relationship change (block,
/// unblock, friend add/remove) so it can keep its own view of the relationship graph in sync.
/// Best-effort only: callers never await delivery and never see failures (see
/// <see cref="RelationshipChangeNotifier"/> for the retry/logging behavior).
/// </summary>
public interface IRelationshipChangeNotifier
{
    void NotifyChange(RelationshipChangeType type, string actor, string target);
}
