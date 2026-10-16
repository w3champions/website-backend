using System.Collections.Generic;
using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Services;

public interface IAdminRoster
{
    /// <summary>
    /// BattleTags of all admins (case-insensitive). Never null; empty if the roster was never loaded.
    /// Never throws: on refresh failure the last good set is served.
    /// </summary>
    Task<IReadOnlySet<string>> GetAdminBattleTags();

    /// <summary>False for null/blank tags without fetching. Never throws.</summary>
    Task<bool> IsAdmin(string battleTag);
}
