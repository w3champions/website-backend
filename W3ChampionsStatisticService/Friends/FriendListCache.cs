using System.Collections.Generic;
using System.Collections.Concurrent;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Friends;

public class FriendlistCache(string battleTag) : IIdentifiable
{
    public string Id { get; set; } = battleTag;
    
    // Keep as List for MongoDB serialization but use HashSet for efficient operations
    public List<string> Friends { get; set; } = new List<string>();
    public List<string> BlockedBattleTags { get; set; } = new List<string>();
    public bool BlockAllRequests { get; set; } = false;
    
    // In-memory cached HashSets for O(1) lookups - not persisted to DB
    private HashSet<string> _friendsSet;
    private HashSet<string> _blockedSet;
    
    // Lazy initialization for efficient lookups
    public HashSet<string> FriendsSet => _friendsSet ??= new HashSet<string>(Friends);
    public HashSet<string> BlockedSet => _blockedSet ??= new HashSet<string>(BlockedBattleTags);
    
    // Efficient O(1) operations
    public bool IsFriend(string battleTag) => FriendsSet.Contains(battleTag);
    public bool IsBlocked(string battleTag) => BlockedSet.Contains(battleTag);
    
    // Update methods that maintain sync between List and HashSet
    public bool AddFriend(string battleTag)
    {
        if (FriendsSet.Add(battleTag))
        {
            Friends.Add(battleTag);
            return true;
        }
        return false;
    }
    
    public bool RemoveFriend(string battleTag)
    {
        if (FriendsSet.Remove(battleTag))
        {
            Friends.Remove(battleTag);
            return true;
        }
        return false;
    }
    
    public bool AddBlocked(string battleTag)
    {
        if (BlockedSet.Add(battleTag))
        {
            BlockedBattleTags.Add(battleTag);
            return true;
        }
        return false;
    }
    
    public bool RemoveBlocked(string battleTag)
    {
        if (BlockedSet.Remove(battleTag))
        {
            BlockedBattleTags.Remove(battleTag);
            return true;
        }
        return false;
    }
}
