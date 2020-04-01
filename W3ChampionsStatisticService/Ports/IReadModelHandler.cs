using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Ports
{
    public interface IReadModelHandler
    {
        Task Update(MatchFinishedEvent nextEvent);
    }
}