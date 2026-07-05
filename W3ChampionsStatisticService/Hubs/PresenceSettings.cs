using System;

namespace W3ChampionsStatisticService.Hubs;

/// <summary>
/// Cutover-only feature flag gating <see cref="WebsiteBackendHub"/>'s legacy
/// "FriendOnlineStatus" presence broadcast (see <c>NotifyFriendsWithIsOnline</c>).
/// Chat-service now owns presence via its own FriendPresenceChanged system; this flag
/// exists purely so the I2 cutover release can retire the hub broadcast once every
/// launcher consumer has migrated off it.
///
/// Driven by the <c>RETIRE_FRIEND_ONLINE_STATUS</c> environment variable: "true"
/// (case-insensitive) retires the broadcast (stop emitting); absent or any other value
/// keeps emitting, which is the current/default behavior and MUST stay the default
/// until the I2 cutover ships.
///
/// Once the cutover has stabilized, this whole pathway (<c>NotifyFriendsWithIsOnline</c>,
/// <see cref="W3ChampionsStatisticService.Friends.FriendResponseType.FriendOnlineStatus"/>,
/// this class, and the env var) becomes dead code and should be physically removed as a
/// post-cutover cleanup item.
/// </summary>
public class PresenceSettings(bool retireFriendOnlineStatus)
{
    public bool RetireFriendOnlineStatus { get; } = retireFriendOnlineStatus;

    public static PresenceSettings FromEnvironment() =>
        new(bool.TryParse(Environment.GetEnvironmentVariable("RETIRE_FRIEND_ONLINE_STATUS"), out var v) && v);
}
