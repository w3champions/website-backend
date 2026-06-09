using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace W3ChampionsStatisticService.Ports;

public interface IProgressionMilestoneRepository
{
    Task<ProgressionMilestone> LoadMilestone(string id);
    Task UpsertMilestone(ProgressionMilestone milestone);

    // Owner-private: all lifetime milestone docs the player participates in — their solo docs and
    // any arranged-team doc whose PlayerIds includes them. Keyed by battleTag from the caller's JWT.
    Task<List<ProgressionMilestone>> LoadMilestonesForPlayer(string battleTag);
}
