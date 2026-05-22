#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.PlayerMatchTelemetry;

/// <summary>
/// Mongo repository for <see cref="PlayerMatchTelemetry"/>. Implements
/// <see cref="IRequiresIndexes"/> so the existing
/// <c>MongoIndexInitializationService</c> creates the TTL and lookup indexes
/// at application startup once this type is registered with DI.
/// </summary>
[Trace]
public class PlayerMatchTelemetryRepository(MongoClient mongoClient)
    : MongoDbRepositoryBase(mongoClient), IPlayerMatchTelemetryRepository, IRequiresIndexes
{
    public string CollectionName => nameof(PlayerMatchTelemetry);

    private IMongoCollection<PlayerMatchTelemetry> Collection
        => CreateCollection<PlayerMatchTelemetry>();

    /// <summary>
    /// Upserts a single player's entry into the per-game document atomically
    /// via a single aggregation-pipeline UpdateOneAsync.
    /// </summary>
    /// <remarks>
    /// One MongoDB round trip: the pipeline initializes top-level fields on insert
    /// (via $ifNull), then either replaces the existing Players entry whose
    /// BattleTag matches (via $map + $cond) or appends a new entry (via
    /// $concatArrays). This eliminates the race window where two concurrent
    /// submissions for the same (gameId, battleTag) could interleave and
    /// produce duplicate array entries.
    /// </remarks>
    public async Task UpsertPlayerEntryAsync(
        long gameId,
        DateTime matchWallStart,
        PlayerMatchTelemetryEntry entry,
        TimeSpan ttl)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now + ttl;
        var newEntryBson = entry.ToBsonDocument();

        var setStage = new BsonDocument("$set", new BsonDocument
        {
            { "_id", gameId },
            {
                "MatchWallStart",
                new BsonDocument("$ifNull", new BsonArray { "$MatchWallStart", matchWallStart })
            },
            {
                "CreatedAt",
                new BsonDocument("$ifNull", new BsonArray { "$CreatedAt", now })
            },
            {
                "ExpiresAt",
                new BsonDocument("$ifNull", new BsonArray { "$ExpiresAt", expiresAt })
            },
            {
                "Players",
                new BsonDocument("$cond", new BsonDocument
                {
                    {
                        "if",
                        new BsonDocument("$in", new BsonArray
                        {
                            entry.BattleTag,
                            new BsonDocument("$ifNull", new BsonArray
                            {
                                "$Players.BattleTag",
                                new BsonArray()
                            })
                        })
                    },
                    {
                        "then",
                        new BsonDocument("$map", new BsonDocument
                        {
                            { "input", "$Players" },
                            { "as", "p" },
                            {
                                "in",
                                new BsonDocument("$cond", new BsonDocument
                                {
                                    {
                                        "if",
                                        new BsonDocument("$eq", new BsonArray
                                        {
                                            "$$p.BattleTag",
                                            entry.BattleTag
                                        })
                                    },
                                    { "then", newEntryBson },
                                    { "else", "$$p" }
                                })
                            }
                        })
                    },
                    {
                        "else",
                        new BsonDocument("$concatArrays", new BsonArray
                        {
                            new BsonDocument("$ifNull", new BsonArray
                            {
                                "$Players",
                                new BsonArray()
                            }),
                            new BsonArray { newEntryBson }
                        })
                    }
                })
            }
        });

        var pipeline = new BsonDocumentStagePipelineDefinition<PlayerMatchTelemetry, PlayerMatchTelemetry>(
            new[] { setStage });
        var filter = Builders<PlayerMatchTelemetry>.Filter.Eq(x => x.GameId, gameId);
        await Collection.UpdateOneAsync(filter, pipeline, new UpdateOptions { IsUpsert = true });
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

            // Recency index — "N most recent telemetry submissions".
            new(
                Builders<PlayerMatchTelemetry>.IndexKeys.Descending(x => x.CreatedAt),
                new CreateIndexOptions { Name = "CreatedAt_recency" }),
        };

        await Collection.Indexes.CreateManyAsync(indexes);
    }
}
