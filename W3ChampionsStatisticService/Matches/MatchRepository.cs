using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Common.Constants;
using W3ChampionsStatisticService.Heroes;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Matches;

[Trace]
public class MatchRepository(MongoClient mongoClient, IOngoingMatchesCache cache) : MongoDbRepositoryBase(mongoClient), IMatchRepository, IRequiresIndexes
{
    private readonly IOngoingMatchesCache _cache = cache;

    public string CollectionName => "Matchup";

    public async Task EnsureIndexesAsync()
    {
        // Matchup collection indexes
        var matchupCollection = CreateCollection<Matchup>();

        var matchupIndexes = new List<CreateIndexModel<Matchup>>
        {
            // Compound index commonly used for listing matches by gateway, season and game mode
            new CreateIndexModel<Matchup>(
                Builders<Matchup>.IndexKeys
                    .Ascending(x => x.GateWay)
                    .Descending(x => x.Season)
                    .Ascending(x => x.GameMode)
            ),

            // Lookups by original ongoing match id and flo id (descending)
            new CreateIndexModel<Matchup>(
                Builders<Matchup>.IndexKeys.Descending(x => x.MatchId),
                new CreateIndexOptions { Unique = false }
            ),
            new CreateIndexModel<Matchup>(
                Builders<Matchup>.IndexKeys.Descending(x => x.FloMatchId),
                new CreateIndexOptions { Unique = false }
            ),

            // Text index for player search across teams
            new CreateIndexModel<Matchup>(
                Builders<Matchup>.IndexKeys
                    .Text(x => x.Team1Players)
                    .Text(x => x.Team2Players)
                    .Text(x => x.Team3Players)
                    .Text(x => x.Team4Players)
            ),

            // Per-player queries: the match-history list (LoadFor) and the
            // opponent search both filter by a player's battleTag and season.
            new CreateIndexModel<Matchup>(
                Builders<Matchup>.IndexKeys
                    .Ascending("Teams.Players.BattleTag")
                    .Descending(x => x.Season)
            )
        };

        await matchupCollection.Indexes.CreateManyAsync(matchupIndexes);

        // MatchFinishedEvent collection indexes for lookups used by this repository
        var finishedEventCollection = CreateCollection<MatchFinishedEvent>();

        var finishedEventIndexes = new List<CreateIndexModel<MatchFinishedEvent>>
        {
            new CreateIndexModel<MatchFinishedEvent>(
                Builders<MatchFinishedEvent>.IndexKeys.Ascending(x => x.match.id)
            ),
            new CreateIndexModel<MatchFinishedEvent>(
                Builders<MatchFinishedEvent>.IndexKeys.Ascending(x => x.match.floGameId)
            ),
            new CreateIndexModel<MatchFinishedEvent>(
                Builders<MatchFinishedEvent>.IndexKeys.Ascending(x => x.match.gamename)
            )
        };

        await finishedEventCollection.Indexes.CreateManyAsync(finishedEventIndexes);
    }

    public async Task Insert(Matchup matchup)
    {
        // TODO: Remove try-catch once we have transactions in Matchmaking service
        try
        {
            await Upsert(matchup, m => m.MatchId == matchup.MatchId);
        }
        catch (MongoCommandException ex) when (ex.Message.Contains("immutable") && ex.Message.Contains("_id"))
        {
            // _id immutable field error - match already exists with different _id, skip it
            Log.Warning("Match {MatchId} already exists (_id conflict), skipping", matchup.MatchId);
        }
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
        int season = 1,
        HeroType hero = HeroType.AllFilter,
        bool playerIncludeRandom = false,
        bool opponentIncludeRandom = false)
    {
        var mongoCollection = CreateCollection<Matchup>();
        var filter = BuildPlayerMatchupFilter(playerId, opponentId, gateWay, gameMode, playerRace, opponentRace, season, hero, playerIncludeRandom, opponentIncludeRandom);
        var matchups = await mongoCollection
            .Find(filter)
            .SortByDescending(s => s.Id)
            .Skip(offset)
            .Limit(pageSize)
            .ToListAsync();

        PlayersObfuscator.ObfuscateMmr(matchups);
        return matchups;
    }

    public Task<long> CountFor(
        string playerId,
        string opponentId = null,
        GateWay gateWay = GateWay.Undefined,
        GameMode gameMode = GameMode.Undefined,
        Race playerRace = Race.Total,
        Race opponentRace = Race.Total,
        int season = 1,
        HeroType hero = HeroType.AllFilter,
        bool playerIncludeRandom = false,
        bool opponentIncludeRandom = false)
    {
        var mongoCollection = CreateCollection<Matchup>();
        var filter = BuildPlayerMatchupFilter(playerId, opponentId, gateWay, gameMode, playerRace, opponentRace, season, hero, playerIncludeRandom, opponentIncludeRandom);
        return mongoCollection.CountDocumentsAsync(filter);
    }

    // Finds the players a given player shares finished matches with, filtered by
    // a (case-insensitive) battleTag fragment and ordered by shared match count.
    // The aggregation only ever touches the player's own matches of that season,
    // so it stays cheap even for very active players.
    //
    // MatchCount, Wins and Losses are scoped to the given game mode (all modes
    // when Undefined), but opponents are still suggested from every mode: the
    // mode-scoped count can be 0 and the client says so, instead of the opponent
    // silently becoming unfindable. Wins/Losses are the searched player's record
    // across the shared matches — allies share the same result.
    public async Task<List<OpponentInfo>> SearchOpponentsFor(
        string battleTag,
        string search,
        int season,
        GateWay gateWay = GateWay.Undefined,
        GameMode gameMode = GameMode.Undefined,
        int limit = 10)
    {
        var mongoCollection = CreateCollection<Matchup>();
        var builder = Builders<Matchup>.Filter;
        var playerMatchesFilter = builder.Where(m => m.Teams.Any(team => team.Players.Any(player => player.BattleTag == battleTag)))
            & builder.Where(m => m.Season == season)
            & builder.Where(m => gateWay == GateWay.Undefined || m.GateWay == gateWay);

        // Everyone the player shared those matches with, except the player
        // themselves; a non-empty search narrows to a case-insensitive fragment,
        // an empty one returns the most played opponents.
        var battleTagCondition = new BsonDocument { { "$ne", battleTag } };
        if (!string.IsNullOrEmpty(search))
        {
            battleTagCondition.Add("$regex", Regex.Escape(search));
            battleTagCondition.Add("$options", "i");
        }
        var opponentFilter = new BsonDocument { { "Teams.Players.BattleTag", battleTagCondition } };

        var inMode = gameMode == GameMode.Undefined
            ? (BsonValue)true
            : new BsonDocument("$eq", new BsonArray { "$GameMode", (int)gameMode });

        var inModeCount = new BsonDocument("$cond", new BsonArray { inMode, 1, 0 });
        var inModeWin = new BsonDocument("$cond", new BsonArray
        {
            new BsonDocument("$and", new BsonArray
            {
                inMode,
                new BsonDocument("$eq", new BsonArray { "$PlayerWon", true })
            }),
            1,
            0
        });

        // Whether the searched player won a match, resolved before the unwinds
        // while the match still has all its teams.
        var playerWon = new BsonDocument("$let", new BsonDocument
        {
            {
                "vars", new BsonDocument("self", new BsonDocument("$arrayElemAt", new BsonArray
                {
                    new BsonDocument("$filter", new BsonDocument
                    {
                        {
                            "input", new BsonDocument("$reduce", new BsonDocument
                            {
                                { "input", "$Teams.Players" },
                                { "initialValue", new BsonArray() },
                                { "in", new BsonDocument("$concatArrays", new BsonArray { "$$value", "$$this" }) }
                            })
                        },
                        { "as", "player" },
                        { "cond", new BsonDocument("$eq", new BsonArray { "$$player.BattleTag", battleTag }) }
                    }),
                    0
                }))
            },
            { "in", "$$self.Won" }
        });

        return await mongoCollection.Aggregate()
            .Match(playerMatchesFilter)
            // Drop everything but the battleTags, mode and the player's result
            // before unwinding so the rest of the pipeline never carries full
            // match documents.
            .Project(new BsonDocument
            {
                { "Teams.Players.BattleTag", 1 },
                { "GameMode", 1 },
                { "PlayerWon", playerWon }
            })
            .Unwind("Teams")
            .Unwind("Teams.Players")
            .Match(opponentFilter)
            .Group(new BsonDocument
            {
                { "_id", "$Teams.Players.BattleTag" },
                { "MatchCount", new BsonDocument("$sum", inModeCount) },
                { "Wins", new BsonDocument("$sum", inModeWin) },
                { "TotalCount", new BsonDocument("$sum", 1) }
            })
            // In-mode opponents first, then whoever shares the most matches overall.
            .Sort(new BsonDocument { { "MatchCount", -1 }, { "TotalCount", -1 }, { "_id", 1 } })
            .Limit(limit)
            .Project(new BsonDocument
            {
                { "_id", 0 },
                { "BattleTag", "$_id" },
                { "MatchCount", 1 },
                { "Wins", 1 },
                { "Losses", new BsonDocument("$subtract", new BsonArray { "$MatchCount", "$Wins" }) }
            })
            .As<OpponentInfo>()
            .ToListAsync();
    }

    private FilterDefinition<Matchup> BuildPlayerMatchupFilter(
        string playerId,
        string opponentId,
        GateWay gateWay,
        GameMode gameMode,
        Race playerRace,
        Race opponentRace,
        int season,
        HeroType hero,
        bool playerIncludeRandom = false,
        bool opponentIncludeRandom = false)
    {
        var builder = Builders<Matchup>.Filter;
        FilterDefinition<Matchup> filter = builder.Empty;

        filter &= builder.Where(m => m.Teams.Any(team => team.Players.Any(player => player.BattleTag == playerId)));
        if (!string.IsNullOrEmpty(opponentId))
        {
            filter &= builder.Where(m => m.Teams.Any(team => team.Players.Any(player => player.BattleTag == opponentId)));
        }
        filter &= builder.Where(m => gameMode == GameMode.Undefined || m.GameMode == gameMode);
        filter &= builder.Where(m => gateWay == GateWay.Undefined || m.GateWay == gateWay);
        if (playerRace != Race.Total)
        {
            filter &= builder.Where(m => m.Teams.Any(t => t.Players.Any(p => p.BattleTag == playerId && (p.Race == playerRace || (playerIncludeRandom && p.Race == Race.RnD && p.RndRace == playerRace)))));
        }

        if (opponentRace != Race.Total)
        {
            filter &= builder.Where(m => m.Teams.Any(t => t.Players.Any(p => p.BattleTag != playerId && (p.Race == opponentRace || (opponentIncludeRandom && p.Race == Race.RnD && p.RndRace == opponentRace)))));
        }

        filter &= builder.Where(m => m.Season == season);
        if (hero != HeroType.AllFilter && hero != HeroType.Unknown)
        {
            filter &= builder.Where(m => m.Teams.Any(t => t.Players.Any(p => p.Heroes.Count > 0 && p.Heroes.Any(h => h.Id == hero))));
        }
        return filter;
    }

    public async Task<MatchupDetail> LoadFinishedMatchDetails(ObjectId id)
    {
        var originalMatch = await LoadFirst<MatchFinishedEvent>(t => t.Id == id);
        var match = await LoadFirst<Matchup>(t => t.Id == id);

        return new MatchupDetail
        {
            Match = match,
            PlayerScores = originalMatch?.result?.players.Select(p => CreateDetail(p)).ToList()
        };
    }

    public async Task<MatchupDetail> LoadFinishedMatchDetailsByMatchId(string id)
    {
        var originalMatch = await LoadFirst<MatchFinishedEvent>(t => t.match.id == id);
        var match = await LoadFirst<Matchup>(t => t.MatchId == id);

        return new MatchupDetail
        {
            Match = match,
            PlayerScores = originalMatch?.result?.players.Select(p => CreateDetail(p)).ToList()
        };
    }

    public async Task<MatchupDetail> LoadFinishedMatchDetailsByFloId(int floMatchId)
    {
        var originalMatch = await LoadFirst<MatchFinishedEvent>(t => t.match.floGameId == floMatchId);
        var match = await LoadFirst<Matchup>(t => t.FloMatchId == floMatchId);

        return new MatchupDetail
        {
            Match = match,
            PlayerScores = originalMatch?.result?.players.Select(p => CreateDetail(p)).ToList()
        };
    }

    public async Task<MatchFinishedEvent> LoadMatchFinishedEventByGameName(string gameName)
    {
        // TODO: Check how frequently this is called as this is not covered by an index.
        return await LoadFirst<MatchFinishedEvent>(t => t.match.gamename == gameName);
    }

    public Task EnsureIndices()
    {
        // Legacy method used in some tests. Delegate to the consolidated index creation.
        return EnsureIndexesAsync();
    }

    private static PlayerScore CreateDetail(PlayerBlizzard playerBlizzard)
    {
        if (playerBlizzard.heroes != null)
        {
            foreach (var player in playerBlizzard.heroes)
            {
                player.icon = player.icon.ParseReforgedName();
            }
        }

        return new PlayerScore(
            playerBlizzard.battleTag,
            playerBlizzard.unitScore,
            playerBlizzard.heroes?.Select(h => new Heroes.Hero(h)).ToList() ?? new List<Heroes.Hero>(),
            playerBlizzard.heroScore,
            playerBlizzard.resourceScore,
            playerBlizzard.teamIndex);
    }

    public async Task<List<Matchup>> Load(int season, GameMode gameMode, int offset = 0, int pageSize = 100, HeroType hero = HeroType.AllFilter, int minMmr = 0, int? maxMmr = null, int? minDuration = null, int? maxDuration = null, string mapName = "Overall")
    {
        if (maxMmr == null) maxMmr = MmrConstants.MaxMmrPerGameMode[gameMode];
        var mongoCollection = CreateCollection<Matchup>();
        var filter = GetLoadFilter(season, gameMode, hero, minMmr, maxMmr, minDuration, maxDuration, mapName);
        var results = await mongoCollection.Find(filter).SortByDescending(s => s.EndTime).Skip(offset).Limit(pageSize).ToListAsync();
        PlayersObfuscator.ObfuscateMmr(results);
        return results;
    }

    public async Task<List<string>> LoadMapNames(int season, GameMode gameMode)
    {
        var builder = Builders<Matchup>.Filter;
        var filter = builder.Eq(m => m.Season, season)
            & builder.Eq(m => m.GameMode, gameMode)
            & builder.Ne(m => m.MapName, null)
            & builder.Ne(m => m.MapName, string.Empty);

        var mapNames = await CreateCollection<Matchup>()
            .Distinct(m => m.MapName, filter)
            .ToListAsync();

        return mapNames.OrderBy(mapName => mapName).ToList();
    }

    // Net MMR movement per ladder entry (player + queued race, so random players pool
    // under RnD) summed from every match a player finished inside the window. The window
    // is applied on _id because ObjectIds carry the insertion timestamp — matchups are
    // written when the match finishes, so this rides the _id index instead of needing a
    // new EndTime index. Placement-grade entries (rank deviation at or above the
    // obfuscation threshold) are excluded, mirroring what the site is willing to show.
    public async Task<List<MmrRiser>> LoadMmrRisers(int season, GameMode gameMode, DateTimeOffset since, int top)
    {
        var mongoCollection = CreateCollection<Matchup>();
        var cutoffId = new ObjectId(((int)since.ToUnixTimeSeconds()).ToString("x8") + "0000000000000000");

        var stages = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "_id", new BsonDocument("$gte", cutoffId) },
                { "Season", season },
                { "GameMode", (int)gameMode },
            }),
            // Chronological order so $last below picks each entry's latest state.
            new BsonDocument("$sort", new BsonDocument("_id", 1)),
            new BsonDocument("$unwind", "$Teams"),
            new BsonDocument("$unwind", "$Teams.Players"),
            new BsonDocument("$match", new BsonDocument
            {
                { "Teams.Players.OldMmr", new BsonDocument("$gt", 0) },
                { "Teams.Players.CurrentMmr", new BsonDocument("$gt", 0) },
                { "Teams.Players.OldRankDeviation", new BsonDocument("$not", new BsonDocument("$gte", PlayersObfuscator.RankDeviationObfuscationThreshold)) },
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument { { "battleTag", "$Teams.Players.BattleTag" }, { "race", "$Teams.Players.Race" } } },
                { "mmrGain", new BsonDocument("$sum", new BsonDocument("$subtract", new BsonArray { "$Teams.Players.CurrentMmr", "$Teams.Players.OldMmr" })) },
                { "games", new BsonDocument("$sum", 1) },
                { "name", new BsonDocument("$last", "$Teams.Players.Name") },
                { "currentMmr", new BsonDocument("$last", "$Teams.Players.CurrentMmr") },
                { "countryCode", new BsonDocument("$last", "$Teams.Players.CountryCode") },
            }),
            // A riser has to have climbed; don't pad short lists with net losers.
            new BsonDocument("$match", new BsonDocument("mmrGain", new BsonDocument("$gt", 0))),
            new BsonDocument("$sort", new BsonDocument("mmrGain", -1)),
            new BsonDocument("$limit", top),
            new BsonDocument("$project", new BsonDocument
            {
                { "_id", 0 },
                { "BattleTag", "$_id.battleTag" },
                { "Race", "$_id.race" },
                { "Name", "$name" },
                { "MmrGain", "$mmrGain" },
                { "Games", "$games" },
                { "CurrentMmr", "$currentMmr" },
                { "CountryCode", "$countryCode" },
            }),
        };

        var pipeline = PipelineDefinition<Matchup, MmrRiser>.Create(stages);
        return await mongoCollection.Aggregate(pipeline).ToListAsync();
    }

    public async Task<int> GetFloIdFromId(string gameId)
    {
        var gameIdObj = new ObjectId($"{gameId}");
        var match = await LoadFirst<Matchup>(x => x.Id == gameIdObj);
        return (match == null || match.FloMatchId == null) ? 0 : match.FloMatchId.Value;
    }

    public Task<long> Count(int season, GameMode gameMode, HeroType hero = HeroType.AllFilter, int minMmr = 0, int? maxMmr = null, int? minDuration = null, int? maxDuration = null, string mapName = "Overall")
    {
        if (maxMmr == null) maxMmr = MmrConstants.MaxMmrPerGameMode[gameMode];
        var filter = GetLoadFilter(season, gameMode, hero, minMmr, maxMmr, minDuration, maxDuration, mapName);
        return CreateCollection<Matchup>().CountDocumentsAsync(filter);
    }

    private FilterDefinition<Matchup> GetLoadFilter(int season, GameMode gameMode, HeroType hero = HeroType.AllFilter, int minMmr = 0, int? maxMmr = null, int? minDuration = null, int? maxDuration = null, string mapName = "Overall")
    {
        if (maxMmr == null) maxMmr = MmrConstants.MaxMmrPerGameMode[gameMode];
        var builder = Builders<Matchup>.Filter;
        var filter = builder.Eq(m => m.GameMode, gameMode) & builder.Eq(m => m.Season, season);

        if (!string.IsNullOrWhiteSpace(mapName) && mapName != "Overall")
        {
            filter &= builder.Eq(m => m.MapName, mapName);
        }

        if (hero != HeroType.AllFilter && hero != HeroType.Unknown)
        {
            var heroFilter = builder.Where(m => m.Teams.Any(t => t.Players.Any(p => p.Heroes.Count > 0 && p.Heroes[0].Id == hero)));
            filter &= heroFilter;
        }

        // Filter by minMmr and maxMmr for any player in any team
        if (minMmr > 0 || maxMmr < MmrConstants.MaxMmrPerGameMode[gameMode])
        {
            filter &= builder.Where(m => m.Teams.Any(team => team.Players.Any(player =>
            (player.OldRankDeviation == null || player.OldRankDeviation < PlayersObfuscator.RankDeviationObfuscationThreshold)
                && player.CurrentMmr >= minMmr
                && player.CurrentMmr <= maxMmr)));
        }

        if (minDuration.HasValue)
        {
            filter &= builder.Gte(
                m => m.Duration,
                TimeSpan.FromSeconds(minDuration.Value));
        }

        if (maxDuration.HasValue)
        {
            filter &= builder.Lte(
                m => m.Duration,
                TimeSpan.FromSeconds(maxDuration.Value));
        }

        return filter;
    }


    public async Task InsertOnGoingMatch(OnGoingMatchup matchup)
    {
        await Upsert(matchup, m => m.MatchId == matchup.MatchId);
        PlayersObfuscator.ObfuscateMmr(matchup);
        _cache.Upsert(matchup);
    }

    public async Task<OnGoingMatchup> LoadOnGoingMatchByMatchId(string matchId)
    {
        var mongoCollection = CreateCollection<OnGoingMatchup>();
        var matchup = await mongoCollection.Find(m => m.MatchId == matchId).FirstOrDefaultAsync();
        PlayersObfuscator.ObfuscateMmr(matchup);
        return matchup;
    }

    public async Task<OnGoingMatchup> LoadOnGoingMatchForPlayer(string playerId)
    {
        var mongoCollection = CreateCollection<OnGoingMatchup>();

        var matchup = await mongoCollection
            .Find(m => m.Team1Players.Contains(playerId)
                    || m.Team2Players.Contains(playerId)
                    || m.Team3Players.Contains(playerId)
                    || m.Team4Players.Contains(playerId)
            )
            .FirstOrDefaultAsync();

        PlayersObfuscator.ObfuscateMmr(matchup);
        return matchup;
    }

    public Task<OnGoingMatchup> TryLoadOnGoingMatchForPlayer(string playerId)
    {
        return _cache.LoadOnGoingMatchForPlayer(playerId);
    }

    public async Task DeleteOnGoingMatch(Matchup matchup)
    {
        await Delete<OnGoingMatchup>(x => x.MatchId == matchup.MatchId);
        _cache.Delete(matchup.MatchId);
    }

    public Task<List<OnGoingMatchup>> LoadOnGoingMatches(
        GameMode gameMode = GameMode.Undefined,
        GateWay gateWay = GateWay.Undefined,
        int offset = 0,
        int pageSize = 100,
        string map = "Overall",
        int minMmr = 0,
        int? maxMmr = null,
        string sort = "startTimeDescending")
    {
        if (maxMmr == null) maxMmr = MmrConstants.MaxMmrPerGameMode[gameMode];
        return _cache.LoadOnGoingMatches(gameMode, gateWay, offset, pageSize, map, minMmr, maxMmr, sort);
    }

    public Task<long> CountOnGoingMatches(
            GameMode gameMode = GameMode.Undefined,
            GateWay gateWay = GateWay.Undefined,
            string map = "Overall",
            int minMmr = 0,
            int? maxMmr = null)
    {
        if (maxMmr == null) maxMmr = MmrConstants.MaxMmrPerGameMode[gameMode];
        return _cache.CountOnGoingMatches(gameMode, gateWay, map, minMmr, maxMmr);
    }

    public Task<Season> LoadLastSeason()
    {
        var mongoCollection = CreateCollection<Season>();
        return mongoCollection.AsQueryable().OrderByDescending(c => c.Id).FirstOrDefaultAsync();
    }

    public async Task<DateTimeOffset?> AddPlayerHeroes(DateTimeOffset startTime, int pageSize)
    {
        var matchupCollection = CreateCollection<Matchup>();
        var finishedMatchCollection = CreateCollection<MatchFinishedEvent>();

        var playerFilter = Builders<PlayerOverviewMatches>.Filter.Exists(p => p.Heroes, true);
        var teamFilter = Builders<Team>.Filter.ElemMatch(t => t.Players, playerFilter);
        var matchupBuilder = Builders<Matchup>.Filter;

        var teamPlayersFilter = matchupBuilder.ElemMatch(m => m.Teams, teamFilter);

        var matchupFilter = matchupBuilder.Lt(m => m.StartTime, startTime) & matchupBuilder.Not(teamPlayersFilter);

        // Get all matchups where all players in a matchup don't have a `Heroes` field from before startTime.
        var matches = await matchupCollection.Find(matchupFilter).SortByDescending(m => m.StartTime).Limit(pageSize).ToListAsync();

        if (!matches.Any())
        {
            return null;
        }

        // Get all matching `MatchFinishedEvent` docs
        var matchIds = matches.Select(m => m.Id).ToList();
        var finishedFilter = Builders<MatchFinishedEvent>.Filter.In(f => f.Id, matchIds);
        var finishedMatchesDict = (await finishedMatchCollection.Find(finishedFilter).ToListAsync()).ToDictionary(f => f.Id);

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
                var bulkResult = await matchupCollection.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false });
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
