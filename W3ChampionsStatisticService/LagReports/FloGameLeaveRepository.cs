using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.LagReports;

[Trace]
public class FloGameLeaveRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IRequiresIndexes
{
    public string CollectionName => "FloGameLeaveReport";

    public async Task EnsureIndexesAsync()
    {
        var collection = CreateCollection<FloGameLeaveReport>();

        var indexes = new List<CreateIndexModel<FloGameLeaveReport>>
        {
            // Upsert key — one document per flo game
            new(Builders<FloGameLeaveReport>.IndexKeys.Ascending(r => r.FloGameId),
                new CreateIndexOptions { Unique = true }),

            // Default sort + date range filter
            new(Builders<FloGameLeaveReport>.IndexKeys.Descending(r => r.CreatedAt)),

            // TTL: mirrors the LagReport retention window
            new(Builders<FloGameLeaveReport>.IndexKeys.Ascending(r => r.UpdatedAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(90) }),
        };

        await collection.Indexes.CreateManyAsync(indexes);
    }

    public async Task<FloGameLeaveReport> GetByFloGameId(int floGameId)
    {
        return await LoadFirst<FloGameLeaveReport>(r => r.FloGameId == floGameId);
    }

    /// <summary>
    /// Record leave data for a game.
    ///
    /// A snapshot-bearing write never gets downgraded by a later snapshot-less one:
    /// the three triggers fire at very different times and only the earliest is
    /// likely to catch the game before flo evicts it, so a later empty result is
    /// almost always "too late", not "nothing happened".
    /// </summary>
    public async Task Upsert(int floGameId, EFloLeaveCaptureTrigger trigger, bool snapshotAvailable, List<FloPlayerLeave> players)
    {
        var collection = CreateCollection<FloGameLeaveReport>();
        var filter = Builders<FloGameLeaveReport>.Filter.Eq(r => r.FloGameId, floGameId);

        var existing = await collection.Find(filter).FirstOrDefaultAsync();
        if (existing != null && existing.SnapshotAvailable && !snapshotAvailable)
        {
            return;
        }

        var update = Builders<FloGameLeaveReport>.Update
            .Set(r => r.Trigger, trigger)
            .Set(r => r.SnapshotAvailable, snapshotAvailable)
            .Set(r => r.Players, players ?? [])
            .Set(r => r.UpdatedAt, DateTime.UtcNow)
            .SetOnInsert(r => r.FloGameId, floGameId)
            .SetOnInsert(r => r.CreatedAt, DateTime.UtcNow);

        await collection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
    }
}
