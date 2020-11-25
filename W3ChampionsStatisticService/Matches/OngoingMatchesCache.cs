using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Matches
{
    public class OngoingMatchesCache : MongoDbRepositoryBase, IOngoingMatchesCache
    {
        public Task<long> Count(GameMode gameMode, GateWay gateWay)
        {
            return CreateCollection<Matchup>().CountDocumentsAsync(m =>
                (gameMode == GameMode.Undefined || m.GameMode == gameMode)
                && (gateWay == GateWay.Undefined || m.GateWay == gateWay));
        }

        public Task<List<OnGoingMatchup>> LoadOnGoingMatches(GameMode gameMode, GateWay gateWay, in int offset, in int pageSize)
        {
            var mongoCollection = CreateCollection<OnGoingMatchup>();

            return mongoCollection
                .Find(m => (gameMode == GameMode.Undefined || m.GameMode == gameMode)
                           && (gateWay == GateWay.Undefined || m.GateWay == gateWay))
                .SortByDescending(s => s.Id)
                .Skip(offset)
                .Limit(pageSize)
                .ToListAsync();
        }

        public Task<OnGoingMatchup> LoadOnGoingMatchForPlayer(string playerId)
        {
            var mongoCollection = CreateCollection<OnGoingMatchup>();
            return mongoCollection
                .Find(m => m.Team1Players.Contains(playerId)
                           || m.Team2Players.Contains(playerId)
                           || m.Team3Players.Contains(playerId)
                           || m.Team4Players.Contains(playerId)
                )
                .FirstOrDefaultAsync();
        }

        public OngoingMatchesCache(MongoClient mongoClient) : base(mongoClient)
        {
        }
    }

    public interface IOngoingMatchesCache
    {
        Task<long> Count(GameMode gameMode, GateWay gateWay);
        Task<List<OnGoingMatchup>> LoadOnGoingMatches(GameMode gameMode, GateWay gateWay, in int offset, in int pageSize);
        Task<OnGoingMatchup> LoadOnGoingMatchForPlayer(string playerId);
    }
}