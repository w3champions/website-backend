using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.Ports
{
    public interface IMatchRepository
    {
        Task<List<Matchup>> Load(int offset = default, int pageSize = 100, int gateWay = 10);
        Task Insert(Matchup matchup);
    }
}