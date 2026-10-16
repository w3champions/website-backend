using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Services;

public interface IAdminRoster
{
    /// <summary>
    /// The current immutable roster view. Never null; <see cref="AdminSnapshot.Empty"/> if the roster was
    /// never loaded. Never throws: on refresh failure the last good view is served.
    /// <para>Call it ONCE per operation and pass the snapshot to every admin decision in that operation:
    /// two calls can straddle a refresh and disagree.</para>
    /// </summary>
    Task<AdminSnapshot> GetSnapshotAsync();
}
