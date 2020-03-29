using System.Threading.Tasks;
using W3ChampionsStatisticService.MatchEvents;

namespace W3ChampionsStatisticService.Ports
{
    public interface IReadModelHandler
    {
        Task Update(MatchFinishedEvent nextEvent);
    }
}