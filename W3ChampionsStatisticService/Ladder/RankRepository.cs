using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json.Serialization;
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

        public Task<List<RankWithProfile>> LoadPlayerOfLeague(int leagueId, int gateWay)
        {
            return JoinWith(rank => rank.League == leagueId && rank.Gateway == gateWay);
        }

        public Task<List<RankWithProfile>> LoadPlayerOfLeagueLike(string searchFor, int gateWay)
        {
            var search = searchFor.ToLower();
            return JoinWith(rank => rank.PlayerId.Contains(search) && rank.Gateway == gateWay);
        }

        private async Task<List<RankWithProfile>> JoinWith(Expression<Func<Rank,bool>> matchExpression)
        {
            var ranks = CreateCollection<Rank>();
            var players = CreateCollection<PlayerOverview>();
            var result = await ranks
                .Aggregate()
                .Match(matchExpression)
                .SortBy(rank => rank.RankNumber)
                .Lookup<Rank, PlayerOverview, RankWithProfile>(players,
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

    public class RankWithProfile
    {
        public int Gateway { get; set; }
        public string Id { get; set; }
        public int League { get; set; }
        public int RankNumber { get; set; }
        public int RankingPoints { get; set; }
        public string PlayerId { get; set; }
        [JsonIgnore]
        public List<PlayerOverview> Players { get; set; }
        public PlayerOverview Player => Players.SingleOrDefault();
    }
}