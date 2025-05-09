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

namespace W3ChampionsStatisticService.Ladder;

public class RankRepository(MongoClient mongoClient, PersonalSettingsProvider personalSettingsProvider) : MongoDbRepositoryBase(mongoClient), IRankRepository
{
    private PersonalSettingsProvider _personalSettingsProvider = personalSettingsProvider;

    public Task<List<Rank>> LoadPlayersOfLeague(int leagueId, int season, GateWay gateWay, GameMode gameMode)
    {
        return JoinWith(Builders<Rank>.Filter.And(
            Builders<Rank>.Filter.Eq(rank => rank.League, leagueId),
            Builders<Rank>.Filter.Eq(rank => rank.Gateway, gateWay),
            Builders<Rank>.Filter.Eq(rank => rank.GameMode, gameMode),
            Builders<Rank>.Filter.Eq(rank => rank.Season, season)
        ));
    }

    public async Task<List<Rank>> LoadPlayersOfCountry(string countryCode, int season, GateWay gateWay, GameMode gameMode)
    {
        // TODO: Don't return entire list and then filter, but apply filter to query directly
        var personalSettings = await _personalSettingsProvider.GetPersonalSettingsAsync();

        var battleTags = personalSettings.Where(ps => (ps.CountryCode ?? ps.Location) == countryCode).Select(ps => ps.Id);

        return await JoinWith(Builders<Rank>.Filter.And(
            Builders<Rank>.Filter.Eq(rank => rank.Gateway, gateWay),
            Builders<Rank>.Filter.Eq(rank => rank.GameMode, gameMode),
            Builders<Rank>.Filter.Eq(rank => rank.Season, season),
            Builders<Rank>.Filter.In(rank => rank.Player1Id, battleTags)
        ));
    }

    public Task<List<Rank>> SearchPlayerOfLeague(string searchFor, int season, GateWay gateWay, GameMode gameMode)
    {
        var filters = new List<FilterDefinition<Rank>>
        {
            Builders<Rank>.Filter.Regex(
            rank => rank.PlayerId,
            new MongoDB.Bson.BsonRegularExpression(searchFor, "i")),
            Builders<Rank>.Filter.Eq(rank => rank.Gateway, gateWay),
            Builders<Rank>.Filter.Eq(rank => rank.Season, season)
        };
        if (gameMode != GameMode.Undefined)
        {
            filters.Add(Builders<Rank>.Filter.Eq(rank => rank.GameMode, gameMode));
        }

        // TODO: add limit
        return JoinWith(Builders<Rank>.Filter.And(filters));
    }

    public async Task<List<PlayerInfoForProxy>> SearchAllPlayersForProxy(string tagSearch)
    {
        // searches through all battletags that have ever played a game on the system - does not return duplicates or AT teams

        var ranksList = await JoinWith(Builders<Rank>.Filter.Regex(
            rank => rank.PlayerId,
            new MongoDB.Bson.BsonRegularExpression(tagSearch, "i"))
        );

        var listOfProxyData = new List<PlayerInfoForProxy>();

        foreach (var rank in ranksList)
        {
            var playerInfo = new PlayerInfoForProxy
            {
                GameMode = rank.GameMode,
                Players = rank.Players
            };

            if (!Equals(playerInfo.GameMode, GameMode.GM_2v2_AT))
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
        return JoinWith(Builders<Rank>.Filter.And(
            Builders<Rank>.Filter.Regex(
            rank => rank.Id,
            new MongoDB.Bson.BsonRegularExpression(searchFor, "i")),
            Builders<Rank>.Filter.Eq(rank => rank.Season, season)
        ));
    }

    public Task<List<LeagueConstellation>> LoadLeagueConstellation(int? season = null)
    {
        return LoadAll(
            season != null ? Builders<LeagueConstellation>.Filter.Eq(l => l.Season, season)
            :  Builders<LeagueConstellation>.Filter.Empty
        );
    }

    private async Task<List<Rank>> JoinWith(FilterDefinition<Rank> matchExpression)
    {
        var aggregator = Aggregate<Rank>()
            .Match(matchExpression)
            .SortBy(rank => rank.RankNumber);

        var lookup = Lookup<Rank, PlayerOverview, Rank>(aggregator,
                rank => rank.PlayerId,
                player => player.Id,
                rank => rank.Players);

        var result = lookup.Match(Builders<Rank>.Filter.Ne(r => r.Players, null));

        return await result.ToListAsync();
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
        return Upsert(season, Builders<Season>.Filter.Eq(s => s.Id, season.Id));
    }

    public Task<List<Season>> LoadSeasons()
    {
        return LoadAll<Season>();
    }

    public Task<List<Rank>> LoadRanksForPlayers(List<string> list, int season)
    {
        return JoinWith(Builders<Rank>.Filter.And(
            Builders<Rank>.Filter.Or(
                Builders<Rank>.Filter.In(r => r.Player1Id, list),
                Builders<Rank>.Filter.In(r => r.Player2Id, list)
            ),
            Builders<Rank>.Filter.Eq(r => r.Season, season)
        ));
    }

}
