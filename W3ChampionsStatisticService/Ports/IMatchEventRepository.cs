using System.Collections.Generic;
using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Ports
{
    public interface IMatchEventRepository
    {
        Task<string> Insert(IList<MatchFinishedEvent> events);
        Task<List<MatchFinishedEvent>> Load(string lastObjectId,  int pageSize = 100);
    }
}