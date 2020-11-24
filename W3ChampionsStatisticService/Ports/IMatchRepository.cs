using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.Ports
{
    public interface IMatchRepository
    {
        Task<List<Matchup>> Load(
            GateWay gateWay = GateWay.Undefined,
            GameMode gameMode = GameMode.Undefined,
            int offset = 0,
            int pageSize = 100);

        Task<long> Count(
            GateWay gateWay = GateWay.Undefined,
            GameMode gameMode = GameMode.Undefined);

        Task Insert(Matchup matchup);

        Task<List<Matchup>> LoadFor(string playerId,
            string opponentId = null,
            GateWay gateWay = GateWay.Undefined,
            GameMode gameMode = GameMode.Undefined,
            int pageSize = 100,
            int offset = 0,
            int season = 1);
        Task<long> CountFor(string playerId,
            string opponentId = null,
            GateWay gateWay = GateWay.Undefined,
            GameMode gameMode = GameMode.Undefined,
            int season = 1);

        Task<MatchupDetail> LoadDetails(ObjectId id);
        Task<MatchupDetail> LoadDetailsByOngoingMatchId(string id);

        void InsertOnGoingMatch(OnGoingMatchup matchup);
        OnGoingMatchup? LoadOnGoingMatchForPlayer(string playerId);
        void DeleteOnGoingMatch(string matchId);

        List<OnGoingMatchup> LoadOnGoingMatches(GameMode gameMode = GameMode.Undefined,
            GateWay gateWay = GateWay.Undefined,
            int offset = 0,
            int pageSize = 100);

        long CountOnGoingMatches(GameMode gameMode = GameMode.Undefined,
            GateWay gateWay = GateWay.Undefined);

        Task EnsureIndices();
    }

    public class MatchupDetail
    {
        public Matchup Match { get; set; }
        public List<PlayerScore> PlayerScores { get; set; }
    }
}