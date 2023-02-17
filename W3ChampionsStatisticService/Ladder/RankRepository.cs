using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.Ladder
{
    public class RankRepository : MongoDbRepositoryBase, IRankRepository
    {
        public RankRepository(MongoClient mongoClient, PersonalSettingsProvider personalSettingsProvider) : base(mongoClient)
        {
            _personalSettingsProvider = personalSettingsProvider;
        }
        private PersonalSettingsProvider _personalSettingsProvider;

        public Task<List<Rank>> LoadPlayersOfLeague(int leagueId, int season, GateWay gateWay, GameMode gameMode)
        {
            return JoinWith(rank =>
                rank.League == leagueId
                && rank.Gateway == gateWay
                && rank.GameMode == gameMode
                && rank.Season == season);
        }

        public async Task<List<Rank>> LoadPlayersOfCountry(string countryCode, int season, GateWay gateWay, GameMode gameMode)
        {
            var personalSettings = await _personalSettingsProvider.GetPersonalSettingsAsync();

            var battleTags = personalSettings.Where(ps => (ps.CountryCode ?? ps.Location) == countryCode).Select(ps => ps.Id);

            return await JoinWith(rank => rank.Gateway == gateWay
                    && rank.GameMode == gameMode
                    && rank.Season == season
                    && (battleTags.Contains(rank.Player1Id) || battleTags.Contains(rank.Player2Id)));
        }

        public Task<List<Rank>> SearchPlayerOfLeague(string searchFor, int season, GateWay gateWay, GameMode gameMode)
        {
            var search = searchFor.ToLower();
            return JoinWith(rank =>
                rank.PlayerId.ToLower().Contains(search)
                && rank.Gateway == gateWay
                && (gameMode == GameMode.Undefined || rank.GameMode == gameMode)
                && rank.Season == season);
        }

        public async Task<List<PlayerInfoForProxy>> SearchAllPlayersForProxy(string tagSearch)
        {
            // searches through all battletags that have ever played a game on the system - does not return duplicates or AT teams

            var search = tagSearch.ToLower();
            var ranksList = await JoinWith(rank => rank.PlayerId.ToLower().Contains(search));

            var listOfProxyData = new List<PlayerInfoForProxy>();

            foreach (var rank in ranksList)
            {
                var playerInfo = new PlayerInfoForProxy();
                playerInfo.GameMode = rank.GameMode;
                playerInfo.Players = rank.Players;

                if (!Enum.Equals(playerInfo.GameMode, GameMode.GM_2v2_AT))
                {
                    if (listOfProxyData.Count > 0)
                    {
                        var foundPlayersTags = new List<string>();

                        foreach (var player in listOfProxyData)
                        {
                            foundPlayersTags.Add(player.Player.PlayerIds.First().BattleTag);
                        }

                        if (!foundPlayersTags.Contains(playerInfo.Player.PlayerIds.First().BattleTag))
                        {
                            listOfProxyData.Add(playerInfo);
                        }
                    }
                    else
                    {
                        listOfProxyData.Add(playerInfo);
                    }
                }
            }

            return listOfProxyData;
        }

        public Task<List<Rank>> LoadPlayerOfLeague(string searchFor, int season)
        {
            var search = searchFor.ToLower();
            return JoinWith(rank => rank.Id.ToLower().Contains(search) && rank.Season == season);
        }

        public Task<List<LeagueConstellation>> LoadLeagueConstellation(int? season = null)
        {
            return LoadAll<LeagueConstellation>(l => season == null || l.Season == season);
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

        public Task UpsertSeason(Season season)
        {
            return Upsert(season, s => s.Id == season.Id);
        }

        public Task<List<Season>> LoadSeasons()
        {
            return LoadAll<Season>();
        }

        public Task<List<Rank>> LoadRanksForPlayers(List<string> list, int season)
        {
            return JoinWith(r => (list.Contains(r.Player1Id) || list.Contains(r.Player2Id)) && r.Season == season);
        }

    }
}