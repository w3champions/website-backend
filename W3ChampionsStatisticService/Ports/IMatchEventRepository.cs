using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.MatchEvents;

namespace W3ChampionsStatisticService.Ports
{
    public interface IMatchEventRepository
    {
        Task<string> InsertAsync(IList<MatchFinishedEvent> events);
        Task<IList<MatchFinishedEvent>> LoadAsync(string lastObjectId,  int pageSize = 100);
    }
}