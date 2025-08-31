using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using W3C.Domain.Common.Exceptions;
using W3C.Domain.Common.Services;

namespace W3ChampionsStatisticService.Common.Services;

/// <summary>
/// Service for handling optimistic concurrency control operations with MongoDB
/// </summary>
public class OptimisticConcurrencyService(ILogger<OptimisticConcurrencyService> logger) : IOptimisticConcurrencyService
{
    private readonly ILogger<OptimisticConcurrencyService> _logger = logger;

    public async Task<bool> TryUpdateWithVersionAsync<T>(IMongoCollection<T> collection, T entity, FilterDefinition<T> filter = null) 
        where T : class, IVersioned
    {
        var currentVersion = entity.Version;
        IncrementVersion(entity);

        // Build filter to include version check
        var versionFilter = Builders<T>.Filter.Eq(nameof(IVersioned.Version), currentVersion);
        var combinedFilter = filter != null 
            ? Builders<T>.Filter.And(filter, versionFilter)
            : versionFilter;

        try
        {
            var result = await collection.ReplaceOneAsync(combinedFilter, entity);
            
            if (result.MatchedCount == 0)
            {
                _logger.LogWarning("Optimistic concurrency conflict detected for entity {EntityType} with version {Version}", 
                    typeof(T).Name, currentVersion);
                
                // Reset version back to original value since update failed
                entity.Version = currentVersion;
                return false;
            }

            _logger.LogDebug("Successfully updated entity {EntityType} from version {OldVersion} to {NewVersion}",
                typeof(T).Name, currentVersion, entity.Version);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during optimistic concurrency update for entity {EntityType}", typeof(T).Name);
            
            // Reset version back to original value since update failed
            entity.Version = currentVersion;
            throw;
        }
    }

    public async Task UpdateWithVersionAsync<T>(IMongoCollection<T> collection, T entity, FilterDefinition<T> filter = null, 
        string entityTypeName = null, string entityId = null) where T : class, IVersioned
    {
        var success = await TryUpdateWithVersionAsync(collection, entity, filter);
        
        if (!success)
        {
            var typeName = entityTypeName ?? typeof(T).Name;
            var id = entityId ?? "Unknown";
            
            throw new ConcurrencyException(typeName, id);
        }
    }

    public void IncrementVersion<T>(T entity) where T : IVersioned
    {
        entity.Version++;
        
        _logger.LogDebug("Incremented version for entity {EntityType} to {Version}", 
            typeof(T).Name, entity.Version);
    }
}