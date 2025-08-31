using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Common.Entities;
using W3C.Domain.Common.Repositories;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Common.Repositories;

/// <summary>
/// MongoDB implementation of audit log repository
/// </summary>
public class AuditLogRepository : MongoDbRepositoryBase, IAuditLogRepository
{
    public AuditLogRepository(MongoClient mongoClient) : base(mongoClient)
    {
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        var collection = CreateCollection<AuditLogEntry>();

        // Index on admin battle tag for admin-specific queries
        var adminIndex = new CreateIndexModel<AuditLogEntry>(
            Builders<AuditLogEntry>.IndexKeys.Ascending(x => x.AdminBattleTag));

        // Index on affected user for user-specific queries
        var userIndex = new CreateIndexModel<AuditLogEntry>(
            Builders<AuditLogEntry>.IndexKeys.Ascending(x => x.AffectedUserId));

        // Index on timestamp for date-based queries (descending for recent first)
        var timestampIndex = new CreateIndexModel<AuditLogEntry>(
            Builders<AuditLogEntry>.IndexKeys.Descending(x => x.Timestamp));

        // Compound index on category and timestamp
        var categoryTimestampIndex = new CreateIndexModel<AuditLogEntry>(
            Builders<AuditLogEntry>.IndexKeys
                .Ascending(x => x.Category)
                .Descending(x => x.Timestamp));

        // Index on entity type and ID for entity-specific queries
        var entityIndex = new CreateIndexModel<AuditLogEntry>(
            Builders<AuditLogEntry>.IndexKeys
                .Ascending(x => x.EntityType)
                .Ascending(x => x.EntityId));

        collection.Indexes.CreateMany(new[]
        {
                adminIndex,
                userIndex,
                timestampIndex,
                categoryTimestampIndex,
                entityIndex
            });
    }

    public async Task Create(AuditLogEntry entry)
    {
        var collection = CreateCollection<AuditLogEntry>();
        await collection.InsertOneAsync(entry);
    }

    public async Task<List<AuditLogEntry>> GetByAdmin(string battleTag, DateTime? fromDate = null, DateTime? toDate = null, int limit = 100)
    {
        var collection = CreateCollection<AuditLogEntry>();
        var filter = Builders<AuditLogEntry>.Filter.Eq(x => x.AdminBattleTag, battleTag);

        if (fromDate.HasValue)
        {
            filter = Builders<AuditLogEntry>.Filter.And(filter,
                Builders<AuditLogEntry>.Filter.Gte(x => x.Timestamp, fromDate.Value));
        }

        if (toDate.HasValue)
        {
            filter = Builders<AuditLogEntry>.Filter.And(filter,
                Builders<AuditLogEntry>.Filter.Lte(x => x.Timestamp, toDate.Value));
        }

        return await collection.Find(filter)
            .Sort(Builders<AuditLogEntry>.Sort.Descending(x => x.Timestamp))
            .Limit(limit)
            .ToListAsync();
    }

    public async Task<List<AuditLogEntry>> GetByAffectedUser(string userId, DateTime? fromDate = null, DateTime? toDate = null, int limit = 100)
    {
        var collection = CreateCollection<AuditLogEntry>();
        var filter = Builders<AuditLogEntry>.Filter.Eq(x => x.AffectedUserId, userId);

        if (fromDate.HasValue)
        {
            filter = Builders<AuditLogEntry>.Filter.And(filter,
                Builders<AuditLogEntry>.Filter.Gte(x => x.Timestamp, fromDate.Value));
        }

        if (toDate.HasValue)
        {
            filter = Builders<AuditLogEntry>.Filter.And(filter,
                Builders<AuditLogEntry>.Filter.Lte(x => x.Timestamp, toDate.Value));
        }

        return await collection.Find(filter)
            .Sort(Builders<AuditLogEntry>.Sort.Descending(x => x.Timestamp))
            .Limit(limit)
            .ToListAsync();
    }

    public async Task<List<AuditLogEntry>> GetByCategory(string category, DateTime? fromDate = null, DateTime? toDate = null, int limit = 100)
    {
        var collection = CreateCollection<AuditLogEntry>();
        var filter = Builders<AuditLogEntry>.Filter.Eq(x => x.Category, category);

        if (fromDate.HasValue)
        {
            filter = Builders<AuditLogEntry>.Filter.And(filter,
                Builders<AuditLogEntry>.Filter.Gte(x => x.Timestamp, fromDate.Value));
        }

        if (toDate.HasValue)
        {
            filter = Builders<AuditLogEntry>.Filter.And(filter,
                Builders<AuditLogEntry>.Filter.Lte(x => x.Timestamp, toDate.Value));
        }

        return await collection.Find(filter)
            .Sort(Builders<AuditLogEntry>.Sort.Descending(x => x.Timestamp))
            .Limit(limit)
            .ToListAsync();
    }

    public async Task<List<AuditLogEntry>> GetRecent(int limit = 100)
    {
        var collection = CreateCollection<AuditLogEntry>();
        return await collection.Find(Builders<AuditLogEntry>.Filter.Empty)
            .Sort(Builders<AuditLogEntry>.Sort.Descending(x => x.Timestamp))
            .Limit(limit)
            .ToListAsync();
    }

    public async Task<List<AuditLogEntry>> GetByEntity(string entityType, string entityId)
    {
        var collection = CreateCollection<AuditLogEntry>();
        var filter = Builders<AuditLogEntry>.Filter.And(
            Builders<AuditLogEntry>.Filter.Eq(x => x.EntityType, entityType),
            Builders<AuditLogEntry>.Filter.Eq(x => x.EntityId, entityId)
        );

        return await collection.Find(filter)
            .Sort(Builders<AuditLogEntry>.Sort.Descending(x => x.Timestamp))
            .ToListAsync();
    }
}