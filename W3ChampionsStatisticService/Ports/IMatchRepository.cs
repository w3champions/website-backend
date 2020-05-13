using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.Ports
{
    public interface IMatchRepository
    {
        Task<List<Matchup>> Load(
            GameMode gameMode = GameMode.Undefined,
            int offset = 0,
            int pageSize = 100);
        Task Insert(Matchup matchup);
        Task<List<Matchup>> LoadFor(string playerId,
            string opponentId = null,
            GateWay gateWay = GateWay.Undefined,
            GameMode gameMode = GameMode.Undefined,
            int pageSize = 100,
            int offset = 0);
        Task<long> Count();
        Task<long> CountFor(string playerId,
            string opponentId = null,
            GateWay gateWay = GateWay.Undefined,
            GameMode gameMode = GameMode.Undefined);

        Task<MatchupDetail> LoadDetails(string id);
    }

    public class MatchupDetail
    {
        public Matchup Match { get; set; }
        public List<PlayerScore> PlayerScores { get; set; }
    }
}