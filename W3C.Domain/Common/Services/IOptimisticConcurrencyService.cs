using System.Threading.Tasks;
using MongoDB.Driver;

namespace W3C.Domain.Common.Services;

/// <summary>
/// Service for handling optimistic concurrency control operations
/// </summary>
public interface IOptimisticConcurrencyService
{
    /// <summary>
    /// Updates an entity with optimistic concurrency control
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="collection">MongoDB collection</param>
    /// <param name="entity">Entity to update</param>
    /// <param name="filter">Additional filter conditions</param>
    /// <returns>True if update succeeded, false if concurrency conflict occurred</returns>
    Task<bool> TryUpdateWithVersionAsync<T>(IMongoCollection<T> collection, T entity, FilterDefinition<T> filter = null)
        where T : class, IVersioned;

    /// <summary>
    /// Updates an entity with optimistic concurrency control and throws exception on conflict
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="collection">MongoDB collection</param>
    /// <param name="entity">Entity to update</param>
    /// <param name="filter">Additional filter conditions</param>
    /// <param name="entityTypeName">Entity type name for exception</param>
    /// <param name="entityId">Entity ID for exception</param>
    Task UpdateWithVersionAsync<T>(IMongoCollection<T> collection, T entity, FilterDefinition<T> filter = null,
        string entityTypeName = null, string entityId = null) where T : class, IVersioned;

    /// <summary>
    /// Increments the version field for an entity
    /// </summary>
    /// <param name="entity">Entity to increment version for</param>
    void IncrementVersion<T>(T entity) where T : IVersioned;
}

/// <summary>
/// Interface for entities that support versioning for optimistic concurrency control
/// </summary>
public interface IVersioned
{
    /// <summary>
    /// Version field for optimistic concurrency control
    /// </summary>
    long Version { get; set; }
}
