using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace W3ChampionsStatisticService.Ports;

public interface IProgressionMilestoneRepository
{
    Task<ProgressionMilestone> LoadMilestone(string id);
    Task UpsertMilestone(ProgressionMilestone milestone);
}
