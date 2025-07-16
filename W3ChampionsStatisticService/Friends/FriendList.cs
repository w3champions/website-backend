using System.Collections.Generic;
using System.Linq;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Friends;

public class Friendlist(string battleTag) : IIdentifiable
{
    public string Id { get; set; } = battleTag;
    
    // MongoDB serialization properties
    public List<string> Friends { get; set; } = new List<string>();
    public List<string> BlockedBattleTags { get; set; } = new List<string>();
    public bool BlockAllRequests { get; set; } = false;
    
    // Read-only cached HashSets for O(1) lookups - lazy initialized once
    private HashSet<string> _friendsSet;
    private HashSet<string> _blockedSet;
    
    // Efficient O(1) lookup operations
    public bool IsFriend(string battleTag)
    {
        _friendsSet ??= Friends.ToHashSet();
        return _friendsSet.Contains(battleTag);
    }
    
    public bool IsBlocked(string battleTag)
    {
        _blockedSet ??= BlockedBattleTags.ToHashSet();
        return _blockedSet.Contains(battleTag);
    }
}
