using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Ladder
{
    public class RankRepository : MongoDbRepositoryBase, IRankRepository
    {
        public RankRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public Task<List<Rank>> LoadPlayerOfLeague(int leagueId, int gateWay)
        {
            return JoinWith(rank => rank.League == leagueId && rank.Gateway == gateWay);
        }

        public Task<List<Rank>> LoadPlayerOfLeagueLike(string searchFor, int gateWay)
        {
            var search = searchFor.ToLower();
            return JoinWith(rank => rank.PlayerId.Contains(search) && rank.Gateway == gateWay);
        }

        private async Task<List<Rank>> JoinWith(Expression<Func<Rank,bool>> matchExpression)
        {
            var ranks = CreateCollection<Rank>();
            var players = CreateCollection<PlayerOverview>();
            var result = await ranks
                .Aggregate()
                .Match(matchExpression)
                .SortBy(rank => rank.RankNumber)
                .Lookup<Rank, PlayerOverview, Rank>(players,
                    rank => rank.PlayerId,
                    player => player.Id,
                    rank => rank.Players)
                .ToListAsync();
            return result.Where(r => r.Player != null).ToList();
        }

        public Task Insert(List<Rank> events)
        {
            return UpsertMany(events);
        }
    }
}