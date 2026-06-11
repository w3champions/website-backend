using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

[Trace]
public class PlayerProgressionRepository(MongoClient mongoClient)
    : MongoDbRepositoryBase(mongoClient), IPlayerProgressionRepository, IRequiresIndexes
{
    // Grand Master and Master are the apex leagues; they are served by the dedicated
    // /api/ladder/apex endpoint, not the per-league progression ladder below.
    private static bool IsApexLeague(int league) => league < (int)ProgressionLeague.Adept;

    public string CollectionName => nameof(PlayerProgression);

    public Task<PlayerProgression> LoadProgression(string id)
    {
        return LoadFirst<PlayerProgression>(id);
    }

    public Task UpsertProgression(PlayerProgression progression)
    {
        return Upsert(progression);
    }

    public Task<List<PlayerProgression>> LoadProgressions(IReadOnlyList<string> ids)
    {
        return LoadAll<PlayerProgression>(p => ids.Contains(p.Id));
    }

    public async Task<List<PlayerProgression>> LoadPlayersByProgressionLeague(
        int season, GameMode gameMode, int league, int division, Race? race, int skip, int take)
    {
        // Apex leagues are served elsewhere; never page their docs through the per-league ladder.
        if (IsApexLeague(league))
        {
            return new List<PlayerProgression>();
        }

        var builder = Builders<PlayerProgression>.Filter;
        var filter = builder.Eq(p => p.Season, season)
            & builder.Eq(p => p.GameMode, gameMode)
            & builder.Eq(p => p.League, league)
            & builder.Eq(p => p.Division, division);

        if (race != null)
        {
            filter &= builder.Eq(p => p.Race, race);
        }

        var collection = CreateCollection<PlayerProgression>();
        return await collection
            .Find(filter)
            .SortByDescending(p => p.Points)
            .Skip(skip)
            .Limit(take)
            .ToListAsync();
    }

    public async Task<List<ProgressionBracketCount>> CountByBracket(int season)
    {
        var collection = CreateCollection<PlayerProgression>();
        var filter = Builders<PlayerProgression>.Filter.Eq(p => p.Season, season)
            & Builders<PlayerProgression>.Filter.Ne(p => p.League, null);
        var grouped = await collection.Aggregate()
            .Match(filter)
            .Group(p => new { p.GameMode, p.League, p.Division },
                g => new { g.Key, Count = g.Count() })
            .ToListAsync();
        return grouped
            .Select(g => new ProgressionBracketCount
            {
                GameMode = g.Key.GameMode,
                League = g.Key.League!.Value,
                Division = g.Key.Division,
                Count = g.Count,
            })
            .ToList();
    }

    public async Task<int?> LoadMaxSeason()
    {
        var collection = CreateCollection<PlayerProgression>();
        // SortByDescending(Season).Limit(1) is served as a cheap reverse scan of the existing
        // Season-leading compound index. A future index refactor must keep Season as a leading
        // (ascending) key or this degrades to a COLLSCAN + in-memory sort.
        var latest = await collection
            .Find(Builders<PlayerProgression>.Filter.Empty)
            .SortByDescending(p => p.Season)
            .Limit(1)
            .FirstOrDefaultAsync();
        return latest?.Season;
    }

    public async Task EnsureIndexesAsync()
    {
        var collection = CreateCollection<PlayerProgression>();

        var keys = Builders<PlayerProgression>.IndexKeys
            .Ascending(p => p.Season)
            .Ascending(p => p.GameMode)
            .Ascending(p => p.League)
            .Ascending(p => p.Division)
            .Ascending(p => p.Race)
            .Descending(p => p.Points);

        var indexes = new List<CreateIndexModel<PlayerProgression>>
        {
            // Compound index backing LoadPlayersByProgressionLeague: equality on
            // Season/GameMode/League/Division/Race, then Points descending for the ranked sort.
            // Without it the per-league ladder query would COLLSCAN a collection that grows one
            // document per entity/mode/race.
            new(keys),
        };

        await collection.Indexes.CreateManyAsync(indexes);
    }
}
