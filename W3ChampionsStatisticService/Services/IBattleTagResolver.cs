using System.Collections.Generic;
using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Services;

public interface IBattleTagResolver
{
    /// <summary>
    /// Resolves an arbitrary-cased BattleTag to its canonical form via identification-service.
    /// Returns canonical-cased BattleTag if the user exists; null if not found.
    /// Cached for 5 minutes (positive and negative results).
    /// </summary>
    Task<string> ResolveCanonical(string input);

    /// <summary>
    /// Bulk variant for admin endpoints that take BattleTag lists.
    /// Returns a map from input → canonical (null if not found).
    /// </summary>
    /// <remarks>
    /// IMPLEMENTATION NOTE: identification-service has no batch /api/users/exists endpoint.
    /// This method makes N parallel single-tag calls. For N &gt; ~50, consider adding a
    /// batch endpoint to identification-service. TODO: track via follow-up issue.
    /// </remarks>
    Task<IDictionary<string, string>> ResolveCanonicalBatch(IEnumerable<string> inputs);
}
