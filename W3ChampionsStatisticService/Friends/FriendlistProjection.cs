using System;
using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.Friends;

/// <summary>Outbound views of a <see cref="Friendlist"/>. Never mutates its input: the loaded
/// Friendlist is a cached object that is also persisted, so filtering it in place would delete
/// the stored blocks.</summary>
public static class FriendlistProjection
{
    /// <summary>A NEW Friendlist with the same Id, Friends and BlockAllRequests, and BlockedBattleTags
    /// minus every admin (case-insensitive, order kept). Admins cannot be blocked, so any such stored
    /// block stays dormant and is never shown. Null in, null out; null lists stay null.</summary>
    public static Friendlist WithoutBlockedAdmins(Friendlist friendlist, IReadOnlySet<string> admins)
    {
        if (friendlist == null)
        {
            return null;
        }

        var adminSet = new HashSet<string>(admins ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
        return new Friendlist(friendlist.Id)
        {
            Friends = friendlist.Friends?.ToList(),
            BlockedBattleTags = friendlist.BlockedBattleTags?.Where(tag => !adminSet.Contains(tag)).ToList(),
            BlockAllRequests = friendlist.BlockAllRequests,
        };
    }
}
