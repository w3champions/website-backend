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
            int pageSize = 100,
            string map = "Overall");

        Task<long> Count(
            GateWay gateWay = GateWay.Undefined,
            GameMode gameMode = GameMode.Undefined,
            string map = "Overall");

        Task Insert(Matchup matchup);

        Task<List<Matchup>> LoadFor(string playerId,
            string opponentId = null,
            GateWay gateWay = GateWay.Undefined,
            GameMode gameMode = GameMode.Undefined,
            Race opponentRace = Race.Total,
            int pageSize = 100,
            int offset = 0,
            int season = 1);
        Task<long> CountFor(string playerId,
            string opponentId = null,
            GateWay gateWay = GateWay.Undefined,
            GameMode gameMode = GameMode.Undefined,
            Race opponentRace = Race.Total,
            int season = 1);

        Task<MatchupDetail> LoadDetails(ObjectId id);
        Task<MatchupDetail> LoadDetailsByOngoingMatchId(string id);

        Task InsertOnGoingMatch(OnGoingMatchup matchup);
        Task<OnGoingMatchup> LoadOnGoingMatchForPlayer(string playerId);
        Task<OnGoingMatchup> TryLoadOnGoingMatchForPlayer(string playerId);
        Task DeleteOnGoingMatch(string matchId);

        Task<List<OnGoingMatchup>> LoadOnGoingMatches(
            GameMode gameMode = GameMode.Undefined,
            GateWay gateWay = GateWay.Undefined,
            int offset = 0,
            int pageSize = 100,
            string map = "Overall");

        Task<long> CountOnGoingMatches(
            GameMode gameMode = GameMode.Undefined,
            GateWay gateWay = GateWay.Undefined,
            string map = "Overall");

        Task EnsureIndices();
    }

    public class MatchupDetail
    {
        public Matchup Match { get; set; }
        public List<PlayerScore> PlayerScores { get; set; }
    }
}