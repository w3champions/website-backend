using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.Ports
{
    public interface IMatchRepository
    {
        Task<List<Matchup>> Load(int offset = 0, int pageSize = 50, int gateWay = 10);
        Task Insert(Matchup matchup);
        Task<List<Matchup>> LoadFor(string playerId, string opponentId = null, int pageSize = 50, int offset = 0);
        Task<long> Count();
        Task<long> CountFor(string playerId, string opponentId = null);
    }
}