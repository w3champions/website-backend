using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Heroes;
using W3ChampionsStatisticService.Ladder;

namespace W3ChampionsStatisticService.Matches;

public class MatchRepository(MongoClient mongoClient, IOngoingMatchesCache cache, ITransactionCoordinator transactionCoordinator) : MongoDbRepositoryBase(mongoClient, transactionCoordinator), IMatchRepository
{
    private readonly IOngoingMatchesCache _cache = cache;

    public Task Insert(Matchup matchup)
    {
        return Upsert(matchup, Builders<Matchup>.Filter.Eq(m => m.MatchId, matchup.MatchId));
    }

    private FilterDefinition<Matchup> BuildMatchupFilter(
        string playerId,
        string opponentId = null,
        GateWay gateWay = GateWay.Undefined,
        GameMode gameMode = GameMode.Undefined,
        Race playerRace = Race.Total,
        Race opponentRace = Race.Total,
        int season = 1)
    {
        var builder = Builders<Matchup>.Filter;
        var filters = new List<FilterDefinition<Matchup>>
        {
            builder.ElemMatch(m => m.Teams, t => t.Players.Any(p => p.BattleTag == playerId)),
            builder.Eq(m => m.Season, season)
        };

        if (!string.IsNullOrEmpty(opponentId))
        {
            filters.Add(builder.ElemMatch(m => m.Teams, t => t.Players.Any(p => p.BattleTag == opponentId)));
        }

        if (gameMode != GameMode.Undefined)
        {
            filters.Add(builder.Eq(m => m.GameMode, gameMode));
        }

        if (gateWay != GateWay.Undefined)
        {
            filters.Add(builder.Eq(m => m.GateWay, gateWay));
        }

        if (playerRace != Race.Total)
        {
            filters.Add(builder.ElemMatch(m => m.Teams, 
                t => t.Players.Any(p => p.Race == playerRace && p.BattleTag == playerId)));
        }

        if (opponentRace != Race.Total)
        {
            filters.Add(builder.ElemMatch(m => m.Teams,
                t => t.Players.Any(p => p.Race == opponentRace && p.BattleTag != playerId)));
        }

        return builder.And(filters);
    }

    public async Task<List<Matchup>> LoadFor(
        string playerId,
        string opponentId = null,
        GateWay gateWay = GateWay.Undefined,
        GameMode gameMode = GameMode.Undefined,
        Race playerRace = Race.Total,
        Race opponentRace = Race.Total,
        int pageSize = 100,
        int offset = 0,
        int season = 1)
    {
        var filter = BuildMatchupFilter(playerId, opponentId, gateWay, gameMode, playerRace, opponentRace, season);
        return await LoadAll(filter, sortBy: Builders<Matchup>.Sort.Descending(m => m.Id), limit: pageSize, offset: offset);
    }

    public async Task<long> CountFor(
        string playerId,
        string opponentId = null,
        GateWay gateWay = GateWay.Undefined,
        GameMode gameMode = GameMode.Undefined,
        Race playerRace = Race.Total,
        Race opponentRace = Race.Total,
        int season = 1)
    {
        var filter = BuildMatchupFilter(playerId, opponentId, gateWay, gameMode, playerRace, opponentRace, season);
        return await Count(filter);
    }

    public async Task<MatchupDetail> LoadDetails(ObjectId id)
    {
        var originalMatch = await LoadFirst(Builders<MatchFinishedEvent>.Filter.Eq(m => m.Id, id));
        var match = await LoadFirst(Builders<Matchup>.Filter.Eq(m => m.Id, id));

        return new MatchupDetail
        {
            Match = match,
            PlayerScores = originalMatch?.result?.players.Select(p => CreateDetail(p)).ToList()
        };
    }

    public async Task<MatchupDetail> LoadDetailsByOngoingMatchId(string id)
    {
        var idObj = new ObjectId(id);
        var originalMatch = await LoadFirst(Builders<MatchFinishedEvent>.Filter.Eq(m => m.Id, idObj));
        var match = await LoadFirst(Builders<Matchup>.Filter.Eq(m => m.MatchId, id));

        return new MatchupDetail
        {
            Match = match,
            PlayerScores = originalMatch?.result?.players.Select(p => CreateDetail(p)).ToList()
        };
    }

    public async Task<MatchFinishedEvent> LoadDetailsByGameName(string gameName)
    {
        return await LoadFirst(Builders<MatchFinishedEvent>.Filter.Eq(m => m.match.gamename, gameName));
    }

    public Task EnsureIndices()
    {
        var matchUpLogBuilder = Builders<Matchup>.IndexKeys;

        var textIndex = new CreateIndexModel<Matchup>(
            matchUpLogBuilder
            .Text(x => x.Team1Players)
            .Text(x => x.Team2Players)
            .Text(x => x.Team3Players)
            .Text(x => x.Team4Players)
        );

        return CreateIndexOneAsync(textIndex);
    }

    private static PlayerScore CreateDetail(PlayerBlizzard playerBlizzard)
    {
        foreach (var player in playerBlizzard.heroes)
        {
            player.icon = player.icon.ParseReforgedName();
        }

        return new PlayerScore(
            playerBlizzard.battleTag,
            playerBlizzard.unitScore,
            playerBlizzard.heroes.Select(h => new Heroes.Hero(h)).ToList(),
            playerBlizzard.heroScore,
            playerBlizzard.resourceScore,
            playerBlizzard.teamIndex);
    }

    public Task<List<Matchup>> Load(int season, GameMode gameMode, int offset = 0, int pageSize = 100, HeroType hero = HeroType.AllFilter)
    {
        var filter = GetLoadFilter(season, gameMode, hero);

        return LoadAll(filter, sortBy: Builders<Matchup>.Sort.Descending(m => m.EndTime), limit: pageSize, offset: offset);
    }

    public async Task<int> GetFloIdFromId(string gameId)
    {
        var gameIdObj = new ObjectId($"{gameId}");
        var match = await LoadFirst(Builders<Matchup>.Filter.Eq(m => m.Id, gameIdObj));
        return (match == null || match.FloMatchId == null) ? 0 : match.FloMatchId.Value;
    }

    public Task<long> Count(int season, GameMode gameMode, HeroType hero = HeroType.AllFilter)
    {
        var filter = GetLoadFilter(season, gameMode, hero);
        return Count(filter);
    }

    private FilterDefinition<Matchup> GetLoadFilter(int season, GameMode gameMode, HeroType hero = HeroType.AllFilter)
    {
        var builder = Builders<Matchup>.Filter;
        var filter = builder.Eq(m => m.GameMode, gameMode) & builder.Eq(m => m.Season, season);

        if (hero != HeroType.AllFilter && hero != HeroType.Unknown)
        {
            var heroFilter = builder.Where(m => m.Teams.Any(t => t.Players.Any(p => p.Heroes.Count > 0 && p.Heroes[0].Id == hero)));
            filter &= heroFilter;
        }

        return filter;
    }

    public async Task InsertOnGoingMatch(OnGoingMatchup matchup)
    {
        await _cache.Upsert(matchup);
        await Upsert(matchup, Builders<OnGoingMatchup>.Filter.Eq(m => m.MatchId, matchup.MatchId));
    }


    public async Task<OnGoingMatchup> LoadOnGoingMatchForPlayer(string playerId)
    {
        return await LoadFirst(
            Builders<OnGoingMatchup>.Filter.Or(
                Builders<OnGoingMatchup>.Filter.ElemMatch(m => m.Team1Players, playerId),
                Builders<OnGoingMatchup>.Filter.ElemMatch(m => m.Team2Players, playerId),
                Builders<OnGoingMatchup>.Filter.ElemMatch(m => m.Team3Players, playerId),
                Builders<OnGoingMatchup>.Filter.ElemMatch(m => m.Team4Players, playerId)
            )
        );
    }

    public async Task<OnGoingMatchup> TryLoadOnGoingMatchForPlayer(string playerId)
    {
        return await _cache.LoadOnGoingMatchForPlayer(playerId);
    }

    public async Task DeleteOnGoingMatch(Matchup matchup)
    {
        await Delete<OnGoingMatchup>(x => x.MatchId == matchup.MatchId);
        await _cache.Delete(matchup);
    }

    public async Task<List<OnGoingMatchup>> LoadOnGoingMatches(
        GameMode gameMode = GameMode.Undefined,
        GateWay gateWay = GateWay.Undefined,
        int offset = 0,
        int pageSize = 100,
        string map = "Overall",
        int minMmr = 0,
        int maxMmr = 3000,
        string sort = "startTimeDescending")
    {
        return await _cache.LoadOnGoingMatches(gameMode, gateWay, offset, pageSize, map, minMmr, maxMmr, sort);
    }

    public async Task<long> CountOnGoingMatches(
        GameMode gameMode = GameMode.Undefined,
        GateWay gateWay = GateWay.Undefined,
        string map = "Overall",
        int minMmr = 0,
        int maxMmr = 3000)
    {
        return await _cache.CountOnGoingMatches(gameMode, gateWay, map, minMmr, maxMmr);
    }

    public Task<Season> LoadLastSeason()
    {
        return AsQueryable<Season>().OrderByDescending(c => c.Id).FirstOrDefaultAsync();
    }

    public async Task<DateTimeOffset?> AddPlayerHeroes(DateTimeOffset startTime, int pageSize)
    {
        var playerFilter = Builders<PlayerOverviewMatches>.Filter.Exists(p => p.Heroes, true);
        var teamFilter = Builders<Team>.Filter.ElemMatch(t => t.Players, playerFilter);
        var matchupBuilder = Builders<Matchup>.Filter;

        var teamPlayersFilter = matchupBuilder.ElemMatch(m => m.Teams, teamFilter);

        var matchupFilter = matchupBuilder.Lt(m => m.StartTime, startTime) & matchupBuilder.Not(teamPlayersFilter);

        // Get all matchups where all players in a matchup don't have a `Heroes` field from before startTime.
        var matches = await LoadAll(matchupFilter, sortBy: Builders<Matchup>.Sort.Descending(m => m.StartTime), limit: pageSize);

        if (!matches.Any())
        {
            return null;
        }

        // Get all matching `MatchFinishedEvent` docs
        var matchIds = matches.Select(m => m.Id).ToList();
        var finishedFilter = Builders<MatchFinishedEvent>.Filter.In(f => f.Id, matchIds);
        var finishedMatchesDict = (await LoadAll(finishedFilter)).ToDictionary(f => f.Id);

        var writes = new List<WriteModel<Matchup>>();
        var anyDocumentsModified = false;

        // For all matchups found, update the doc in memory and create a WriteModel to update the doc
        foreach (var match in matches)
        {
            if (!finishedMatchesDict.TryGetValue(match.Id, out var finished))
            {
                Log.Warning("Missing MatchFinishedData for {@matchId}", match.Id);
                continue;
            }

            var finishedPlayersDict = finished?.result?.players?.ToDictionary(p => p.battleTag);
            if (finishedMatchesDict == null)
            {
                Log.Warning("Missing player data in finished match {@matchId}", match.Id);
                continue;
            }

            var isMatchModified = false;

            foreach (var team in match.Teams)
            {
                foreach (var player in team.Players)
                {
                    if (player.Heroes != null && player.Heroes.Any())
                    {
                        Log.Warning("Player {@player} in match {@matchId} already has hero data", player.BattleTag, match.Id);
                        continue;
                    }
                    // Only update if the result.player has heroes
                    if (
                        finishedPlayersDict.TryGetValue(player.BattleTag, out var finishedPlayer)
                        && finishedPlayer.heroes != null
                        && finishedPlayer.heroes.Any()
                    )
                    {
                        player.Heroes = finishedPlayer.heroes.Select(h => new Heroes.Hero(h)).ToList();
                        isMatchModified = true;
                    }
                }
            }

            if (isMatchModified)
            {
                anyDocumentsModified = true;
                var filter = Builders<Matchup>.Filter.Eq(m => m.Id, match.Id);
                writes.Add(new ReplaceOneModel<Matchup>(filter, match));
            }
        }

        if (writes.Any())
        {
            try
            {
                var bulkResult = await BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false });
                Log.Information(
                    "Updated Matchup Finished. Matched: {@matchedCount}, Modified: {@modifiedCount}",
                    bulkResult.MatchedCount,
                    bulkResult.ModifiedCount
                );
            }
            catch (MongoBulkWriteException bwe)
            {
                Log.Error(bwe, "Bulk Replace Error during Matchup update");
            }
        }
        else if (anyDocumentsModified)
        {
            Log.Warning("Matchup documents were modified in memory but not updated in the database. Matches Count: {@count}", matches.Count);
        }

        return matches.LastOrDefault()?.StartTime;
    }
}
