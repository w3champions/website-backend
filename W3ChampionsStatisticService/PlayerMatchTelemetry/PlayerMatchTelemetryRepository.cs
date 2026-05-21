#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.PlayerMatchTelemetry;

// Spec: docs/superpowers/specs/2026-05-21-flo-action-latency-design.md §4.8.2 + §4.8.4.
// _id == GameId. Per-player entries merged into Players[] via idempotent upsert.
[Trace]
public class PlayerMatchTelemetryRepository(MongoClient mongoClient)
    : MongoDbRepositoryBase(mongoClient), IPlayerMatchTelemetryRepository, IRequiresIndexes
{
    public string CollectionName => nameof(PlayerMatchTelemetry);

    private IMongoCollection<PlayerMatchTelemetry> Collection
        => CreateCollection<PlayerMatchTelemetry>();

    public async Task UpsertPlayerEntryAsync(
        long gameId,
        DateTime matchWallStart,
        int bucketMs,
        PlayerMatchTelemetryEntry entry,
        TimeSpan ttl)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<PlayerMatchTelemetry>.Filter.Eq(x => x.GameId, gameId);

        // 1. Initial upsert — establish top-level fields exactly once.
        var initUpdate = Builders<PlayerMatchTelemetry>.Update
            .SetOnInsert(x => x.GameId, gameId)
            .SetOnInsert(x => x.MatchWallStart, matchWallStart)
            .SetOnInsert(x => x.BucketMs, bucketMs)
            .SetOnInsert(x => x.CreatedAt, now)
            .SetOnInsert(x => x.ExpiresAt, now + ttl)
            .SetOnInsert(x => x.Players, new List<PlayerMatchTelemetryEntry>());
        await Collection.UpdateOneAsync(filter, initUpdate, new UpdateOptions { IsUpsert = true });

        // 2. Remove any existing entry for this BattleTag — keeps the merge idempotent
        //    when the same player resubmits (e.g. after a crash recovery).
        var pull = Builders<PlayerMatchTelemetry>.Update.PullFilter(
            x => x.Players,
            Builders<PlayerMatchTelemetryEntry>.Filter.Eq(p => p.BattleTag, entry.BattleTag));
        await Collection.UpdateOneAsync(filter, pull);

        // 3. Push the new entry.
        var push = Builders<PlayerMatchTelemetry>.Update.Push(x => x.Players, entry);
        await Collection.UpdateOneAsync(filter, push);
    }

    public async Task<PlayerMatchTelemetry?> GetByGameIdAsync(long gameId)
    {
        return await Collection
            .Find(Builders<PlayerMatchTelemetry>.Filter.Eq(x => x.GameId, gameId))
            .FirstOrDefaultAsync();
    }

    public async Task EnsureIndexesAsync()
    {
        var indexes = new List<CreateIndexModel<PlayerMatchTelemetry>>
        {
            // TTL index — purges documents `expireAfterSeconds: 0` once ExpiresAt is reached.
            new(
                Builders<PlayerMatchTelemetry>.IndexKeys.Ascending(x => x.ExpiresAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }),

            // Secondary lookup index — "all matches for player X by recency".
            new(
                Builders<PlayerMatchTelemetry>.IndexKeys
                    .Ascending("Players.BattleTag")
                    .Descending(x => x.MatchWallStart),
                new CreateIndexOptions { Name = "Players_BattleTag_MatchWallStart" }),
        };

        await Collection.Indexes.CreateManyAsync(indexes);
    }
}
