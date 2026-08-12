using System;
using MongoDB.Bson;

namespace W3ChampionsStatisticService.Friends;

public class FriendRequest(string sender, string receiver)
{
    public ObjectId Id { get; set; }
    public string Sender { get; set; } = sender;
    public string Receiver { get; set; } = receiver;
    // Both nullable so requests created before these fields existed deserialize
    // as "unknown" instead of a fabricated value. CreatedAt is stamped
    // server-side on creation (FriendCommandHandler) — a client-supplied value
    // is overwritten. SeenAt is null until the receiver acknowledges having
    // seen the request via MarkIncomingFriendRequestsSeen.
    public DateTime? CreatedAt { get; set; }
    public DateTime? SeenAt { get; set; }
}
