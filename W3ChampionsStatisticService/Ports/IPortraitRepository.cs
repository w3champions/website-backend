using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Admin.Portraits;

namespace W3ChampionsStatisticService.Admin
{
    public interface IPortraitRepository
    {
        public Task<List<PortraitDefinition>> LoadPortraitDefinitions();
        public Task SaveNewPortraitDefinitions(List<int> portraitIds);
        public Task DeletePortraitDefinitions(List<int> portraitIds);
    }
}