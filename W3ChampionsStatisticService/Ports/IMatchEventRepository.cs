using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.MatchEvents;

namespace W3ChampionsStatisticService.Ports
{
    public interface IMatchEventRepository
    {
        Task Insert(IEnumerable<MatchFinishedEvent> events);
        Task<IEnumerable<MatchFinishedEvent>> Load();
    }
}