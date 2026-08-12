namespace W3ChampionsStatisticService.Friends;

// Wire values for FriendChange.ChangeType. String constants rather than an
// enum so the serialized value is stable and self-describing for every client.
public static class FriendChangeType
{
    public const string RequestReceived = "RequestReceived";
    public const string RequestRetracted = "RequestRetracted";
    public const string RequestAccepted = "RequestAccepted";
}

/// <summary>
/// A discriminated live friend event, pushed as <c>FriendChangeEvent</c> to the
/// player it affects. <c>FriendResponseData</c> stays the state carrier; this
/// message names WHAT changed and WHO did it — which the argument null-shape of
/// <c>FriendResponseData</c> only implies, and a server refactor could silently
/// change. <c>Actor</c> is the other player's battleTag: the one who sent,
/// retracted, or accepted.
/// </summary>
public class FriendChange(string changeType, string actor)
{
    public string ChangeType { get; set; } = changeType;
    public string Actor { get; set; } = actor;
}
