using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;

namespace W3ChampionsStatisticService.ReadModelBase;

public interface IReadModelHandler
{
    Task Update(MatchFinishedEvent nextEvent);
}
