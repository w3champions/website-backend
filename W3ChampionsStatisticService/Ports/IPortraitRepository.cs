using System.Collections.Generic;
using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Rewards.Portraits;

public interface IPortraitRepository
{
    public Task<List<PortraitDefinition>> LoadPortraitDefinitions();
    public Task SaveNewPortraitDefinitions(List<int> _ids, List<string> _groups = null);
    public Task DeletePortraitDefinitions(List<int> _ids);
    public Task UpdatePortraitDefinition(List<int> _ids, List<string> _groups);
    public Task<List<PortraitGroup>> LoadDistinctPortraitGroups();
}
