using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Ladder
{
    public class RankRepository : MongoDbRepositoryBase, IRankRepository
    {
        public RankRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public Task<List<Rank>> LoadPlayersOfLeague(int leagueId, int season, GateWay gateWay, GameMode gameMode)
        {
            return JoinWith(rank => rank.League == leagueId && rank.Gateway == gateWay && rank.GameMode == gameMode);
        }

        public Task<List<Rank>> SearchPlayerOfLeague(string searchFor, GateWay gateWay, GameMode gameMode)
        {
            var search = searchFor.ToLower();
            return JoinWith(rank => rank.PlayerIdToLower.Contains(search) && rank.Gateway == gateWay && rank.GameMode == gameMode);
        }

        public async Task<List<Rank>> LoadPlayerOfLeague(string searchFor, int season)
        {
            var search = searchFor.ToLower();
            var joinWith = await JoinWith(rank => rank.Id.ToLower().Contains(search) && rank.Season == season);
            return joinWith;
        }

        public Task<List<LeagueConstellation>> LoadLeagueConstellation(int season)
        {
            return LoadAll<LeagueConstellation>(l => l.Season == season);
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

        public Task InsertRanks(List<Rank> events)
        {
            return UpsertMany(events);
        }

        public Task InsertLeagues(List<LeagueConstellation> leagueConstellations)
        {
            return UpsertMany(leagueConstellations);
        }
    }
}