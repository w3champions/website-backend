using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Admin.Portraits;

namespace W3ChampionsStatisticService.Admin
{
    public interface IPortraitRepository
    {
        public Task<PortraitDefinitions> GetPortraits();
        public Task SaveNewPortraits(List<int> portraitIds);
        public Task DeletePortraits(List<int> portraitIds);
    }
}