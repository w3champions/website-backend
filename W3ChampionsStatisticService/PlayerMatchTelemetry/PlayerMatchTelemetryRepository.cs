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
/// <summary>
/// Mongo repository for <see cref="PlayerMatchTelemetry"/>. Implements
/// <see cref="IRequiresIndexes"/> so the existing
/// <c>MongoIndexInitializationService</c> creates the TTL and lookup indexes
/// at application startup — but only after this type is registered with DI.
/// </summary>
/// <remarks>
/// DI registration is wired in Task 1.18 (see
/// docs/superpowers/plans/2026-05-21-flo-action-latency-01-foundation.md).
/// Until then, <see cref="EnsureIndexesAsync"/> must be called explicitly
/// (the integration tests do this).
/// </remarks>
[Trace]
public class PlayerMatchTelemetryRepository(MongoClient mongoClient)
    : MongoDbRepositoryBase(mongoClient), IPlayerMatchTelemetryRepository, IRequiresIndexes
{
    public string CollectionName => nameof(PlayerMatchTelemetry);

    private IMongoCollection<PlayerMatchTelemetry> Collection
        => CreateCollection<PlayerMatchTelemetry>();

    /// <summary>
    /// Upserts a single player's entry into the per-game document. Uses a 3-step pattern
    /// (init top-level via SetOnInsert → pull existing entry by BattleTag → push new entry),
    /// mirroring <see cref="W3ChampionsStatisticService.LagReports.LagReportRepository"/>.
    /// </summary>
    /// <remarks>
    /// The 3-step sequence is NOT atomic. Concurrent invocations for the same
    /// <paramref name="gameId"/> and <c>entry.BattleTag</c> can interleave between
    /// the pull and push steps and produce duplicate array entries.
    /// This is acceptable under the deployment assumption that one launcher
    /// instance per player submits at most once per game (fire-and-forget, no retry —
    /// see spec §4.7 and §4.8.4). If concurrent same-(gameId, battleTag) submissions
    /// become possible, replace this with an arrayFilters-based atomic update.
    /// </remarks>
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
