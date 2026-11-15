using MongoDB.Bson;
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
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Ladder;

[Trace]
public class RankRepository(MongoClient mongoClient, PersonalSettingsProvider personalSettingsProvider) : MongoDbRepositoryBase(mongoClient), IRankRepository, IRequiresIndexes
{
    private PersonalSettingsProvider _personalSettingsProvider = personalSettingsProvider;

    public string CollectionName => nameof(Rank);

    /// <summary>
    /// Declares the Rank index the member lookups seek on — <see cref="LoadLadderStandings"/>
    /// (search ordering) and the context overload of <see cref="LoadRanksForPlayers(List{string}, int, GateWay, GameMode)"/>
    /// (display). Rows written before <see cref="Rank.MemberIds"/> existed stay invisible to those
    /// lookups until the rank-member-ids-backfill admin job (<see cref="RankMemberIdsBackfillJob"/>)
    /// has run — a row only rewrites itself when its league next syncs, which for a finished season
    /// never happens.
    /// </summary>
    /// <remarks>
    /// Creation is idempotent, so redeploys are a no-op. Should prod hold this key under a different
    /// name, createIndexes raises IndexOptionsConflict; MongoIndexInitializationService logs it per
    /// repository and continues — correct results, collection scans, visible only in the startup log.
    /// The index is multikey — one entry per member, which is what lets it answer for every member —
    /// and Background is inert on MongoDB 4.2+, set to match the sibling repositories. The
    /// fallback-carrying queries (<see cref="LoadPlayersOfCountry"/> and the season-only
    /// <see cref="LoadRanksForPlayers(List{string}, int)"/>) are not member-served by it: their $or
    /// keeps them on the season index, exactly as before this field existed.
    /// </remarks>
    public async Task EnsureIndexesAsync()
    {
        var ranks = CreateCollection<Rank>();
        var indexes = new List<CreateIndexModel<Rank>>
        {
            // Keyed lookup for "rank for this battleTag in {season, gateway, gameMode}". Leading with
            // the member lets the bounded $in jump straight to the matching rows instead of scanning
            // the season/mode bucket.
            new(
                Builders<Rank>.IndexKeys
                    .Ascending(r => r.MemberIds)
                    .Ascending(r => r.Season)
                    .Ascending(r => r.Gateway)
                    .Ascending(r => r.GameMode),
                new CreateIndexOptions { Name = "MemberIds_Season_Gateway_GameMode", Background = true }),
        };

        await ranks.Indexes.CreateManyAsync(indexes);
    }

    /// <summary>
    /// Every season that holds rank rows, ascending — the work list of the
    /// rank-member-ids-backfill admin job.
    /// </summary>
    public async Task<List<int>> LoadRankSeasons()
    {
        var ranks = CreateCollection<Rank>();
        var seasons = await ranks.DistinctAsync(r => r.Season, FilterDefinition<Rank>.Empty);
        var list = await seasons.ToListAsync();
        list.Sort();
        return list;
    }

    /// <summary>
    /// Fills <see cref="Rank.MemberIds"/> on the season's rows written before the field existed and
    /// returns how many rows were missing it. One season is one batch of the
    /// rank-member-ids-backfill admin job, which paces between calls.
    /// </summary>
    /// <remarks>
    /// Members come from the joined PlayerOverview, which carries the whole team as structured data,
    /// so teams of three and four are repaired rather than left at the two members the old fields
    /// held. Rows with no PlayerOverview fall back to those two fields; they are the orphan ranks the
    /// lookup drops anyway, so no query can return them either way. Reading the row id instead would
    /// be guesswork — a battleTag may itself contain the separator. The missing-field match makes a
    /// season safe to redo: a filled row no longer matches, so re-running finds nothing.
    /// </remarks>
    public async Task<long> BackfillMemberIds(int season)
    {
        var ranks = CreateCollection<Rank>();
        var missingField = Builders<Rank>.Filter.Eq(r => r.Season, season)
            & Builders<Rank>.Filter.Exists(r => r.MemberIds, false);
        var missing = await ranks.CountDocumentsAsync(missingField);
        if (missing == 0)
        {
            return 0;
        }

        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "Season", season },
                { "MemberIds", new BsonDocument("$exists", false) },
            }),
            new BsonDocument("$lookup", new BsonDocument
            {
                { "from", nameof(PlayerOverview) },
                { "localField", "_id" },
                { "foreignField", "_id" },
                { "as", "overview" },
            }),
            // $project keeps _id + MemberIds alone, and $merge whenMatched:merge writes only those.
            new BsonDocument("$project", new BsonDocument("MemberIds", new BsonDocument("$cond", new BsonArray
            {
                new BsonDocument("$gt", new BsonArray { new BsonDocument("$size", "$overview"), 0 }),
                new BsonDocument("$map", new BsonDocument
                {
                    { "input", new BsonDocument("$arrayElemAt", new BsonArray { "$overview.PlayerIds", 0 }) },
                    { "as", "member" },
                    { "in", "$$member.BattleTag" },
                }),
                new BsonDocument("$filter", new BsonDocument
                {
                    { "input", new BsonArray { "$Player1Id", "$Player2Id" } },
                    { "cond", new BsonDocument("$ne", new BsonArray { "$$this", BsonNull.Value }) },
                }),
            }))),
            new BsonDocument("$merge", new BsonDocument
            {
                { "into", nameof(Rank) },
                { "on", "_id" },
                { "whenMatched", "merge" },
                { "whenNotMatched", "discard" },
            }),
        };

        await ranks.AggregateAsync<BsonDocument>(pipeline);
        return missing;
    }

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

        var battleTags = personalSettings.Where(ps => (ps.CountryCode ?? ps.Location) == countryCode).Select(ps => ps.Id).ToList();

        // The two-field match stays as a fallback: this surface has no rollback flag, so it must keep
        // working on rows the MemberIds backfill has not reached. Remove with the legacy retirement.
        return await JoinWith(rank => rank.Gateway == gateWay
                && rank.GameMode == gameMode
                && rank.Season == season
                && (rank.MemberIds.Any(member => battleTags.Contains(member))
                    || battleTags.Contains(rank.Player1Id) || battleTags.Contains(rank.Player2Id)));
    }

    public Task<List<Rank>> SearchPlayerOfLeague(string searchFor, int season, GateWay gateWay, GameMode gameMode)
    {
        return JoinWith(rank =>
            rank.PlayerId.Contains(searchFor, StringComparison.CurrentCultureIgnoreCase)
            && rank.Gateway == gateWay
            && (gameMode == GameMode.Undefined || rank.GameMode == gameMode)
            && rank.Season == season);
    }

    public async Task<List<PlayerInfoForProxy>> SearchAllPlayersForProxy(string tagSearch)
    {
        // searches through all battletags that have ever played a game on the system - does not return duplicates or AT teams

        var ranksList = await JoinWith(rank => rank.PlayerId.Contains(tagSearch, StringComparison.CurrentCultureIgnoreCase));

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
        return JoinWith(rank => rank.Id.Contains(searchFor, StringComparison.CurrentCultureIgnoreCase) && rank.Season == season);
    }

    public Task<List<LeagueConstellation>> LoadLeagueConstellation(int? season = null)
    {
        return LoadAll<LeagueConstellation>(l => season == null || l.Season == season);
    }

    private async Task<List<Rank>> JoinWith(Expression<Func<Rank, bool>> matchExpression)
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
        // Two-field fallback as in LoadPlayersOfCountry: clan and chat call this with no rollback
        // flag in front of them, so un-backfilled rows must stay findable.
        return JoinWith(r => (r.MemberIds.Any(member => list.Contains(member))
            || list.Contains(r.Player1Id) || list.Contains(r.Player2Id)) && r.Season == season);
    }

    // The one definition of "holds a rank on this ladder". Ordering (LoadLadderStandings) and
    // display (LoadRanksForPlayers) both match on it, so the two stages of a search cannot disagree
    // about who is ranked.
    private static Expression<Func<Rank, bool>> OnLadder(List<string> list, int season, GateWay gateWay, GameMode gameMode)
    {
        return r => r.MemberIds.Any(member => list.Contains(member))
            && r.Season == season
            && r.Gateway == gateWay
            && r.GameMode == gameMode;
    }

    public Task<List<Rank>> LoadRanksForPlayers(List<string> list, int season, GateWay gateWay, GameMode gameMode)
    {
        return JoinWith(OnLadder(list, season, gateWay, gameMode));
    }

    /// <summary>
    /// The same rows as <see cref="LoadRanksForPlayers(List{string}, int, GateWay, GameMode)"/>,
    /// projected to ladder standing alone. For callers that order by standing and never read the
    /// player's stats — the search core asks this for an entire match set, so the joined
    /// PlayerOverview would be payload per hit that nothing opens.
    /// </summary>
    public async Task<List<PlayerLadderStanding>> LoadLadderStandings(
        List<string> list,
        int season,
        GateWay gateWay,
        GameMode gameMode)
    {
        var ranks = CreateCollection<Rank>();
        var players = CreateCollection<PlayerOverview>();
        return await ranks
            .Aggregate()
            .Match(OnLadder(list, season, gateWay, gameMode))
            // The join stays: dropping ranks with no PlayerOverview is what keeps this in agreement
            // with LoadRanksForPlayers about who is ranked. Those orphans are real — 379 in
            // season 13 / 2v2 AT alone.
            .Lookup<Rank, PlayerOverview, Rank>(players,
                rank => rank.PlayerId,
                player => player.Id,
                rank => rank.Players)
            .Match(r => r.Players.Any())
            .Project(r => new PlayerLadderStanding
            {
                MemberIds = r.MemberIds,
                RankingPoints = r.RankingPoints,
            })
            .ToListAsync();
    }
}
