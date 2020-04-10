using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ladder;

namespace W3ChampionsStatisticService.Ports
{
    public interface IRankRepository
    {
        Task<List<Rank>> LoadPlayerOfLeague(int leagueId, int gateWay);
        Task Insert(List<Rank> events);
        Task<List<Rank>> LoadPlayerOfLeagueLike(string searchFor, int gateWay);
    }
}