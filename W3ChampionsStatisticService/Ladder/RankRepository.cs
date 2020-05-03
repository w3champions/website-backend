using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Ladder
{
    public class RankRepository : MongoDbRepositoryBase, IRankRepository
    {
        public RankRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public Task<List<Rank>> LoadPlayersOfLeague(int leagueId, int gateWay)
        {
            return JoinWith(rank => rank.League == leagueId && rank.Gateway == gateWay);
        }

        public Task<List<Rank>> LoadPlayerOfLeagueLike(string searchFor, int gateWay, GameMode gameMode)
        {
            var search = searchFor.ToLower();
            return JoinWith(rank => rank.PlayerId.Contains(search) && rank.Gateway == gateWay && rank.GameMode == gameMode);
        }

        public async Task<Rank> LoadPlayerOfLeague(string searchFor, GameMode gameMode)
        {
            var search = searchFor.ToLower();
            var joinWith = await JoinWith(rank => rank.Id.Contains(search) && rank.GameMode == gameMode);
            return joinWith.FirstOrDefault();
        }

        public async Task<List<LeagueConstellationChangedEvent>> LoadLeagueConstellation()
        {
            var mongoCollection = CreateCollection<LeagueConstellationChangedEvent>();
            var us = await mongoCollection.Find(l => l.gateway == 10).SortByDescending(s => s.id)
                .FirstOrDefaultAsync();
            var eu = await mongoCollection.Find(l => l.gateway == 20).SortByDescending(s => s.id)
                .FirstOrDefaultAsync();
            return new List<LeagueConstellationChangedEvent> { us, eu };
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

        public Task InsertMany(List<Rank> events)
        {
            return UpsertMany(events);
        }
    }
}