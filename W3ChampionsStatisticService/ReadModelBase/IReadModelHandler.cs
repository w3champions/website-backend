using System.Threading.Tasks;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.ReadModelBase
{
    public interface IReadModelHandler
    {
        Task Update(MatchFinishedEvent nextEvent);
    }
}