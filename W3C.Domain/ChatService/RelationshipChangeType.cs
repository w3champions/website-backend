namespace W3C.Domain.ChatService;

/// <summary>
/// The kinds of player-relationship change the website-backend pings chat-service about (see
/// <see cref="IRelationshipChangeNotifier"/>). Wire literals (lowerCamelCase, pinned by the
/// change-ping contract) are mapped in <see cref="RelationshipChangeNotifier"/>, not here — this
/// enum stays a plain C# type so callers use idiomatic PascalCase members.
/// </summary>
public enum RelationshipChangeType
{
    Block,
    Unblock,
    FriendAdd,
    FriendRemove
}
