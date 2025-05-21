using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;

namespace W3ChampionsStatisticService.ReadModelBase;

public interface IMatchFinishedReadModelHandler
{
    Task Update(MatchFinishedEvent nextEvent);
}

public interface IMatchCanceledReadModelHandler
{
    Task Update(MatchCanceledEvent nextEvent);
}

