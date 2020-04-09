using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ladder;

namespace W3ChampionsStatisticService.Ports
{
    public interface IRankeRepository
    {
        Task<List<RankWithProfile>> LoadPlayerOfLeague(int leagueId, int gateWay);
        Task Insert(List<Rank> events);
    }
}