using System.Threading.Tasks;

namespace W3C.Domain.Repositories;

/// <summary>
/// Interface for MongoDB repositories that require index creation.
/// Implementing repositories define their indexes but don't create them in constructors.
/// Index creation is handled once at application startup by MongoIndexInitializationService.
/// </summary>
public interface IRequiresIndexes
{
    /// <summary>
    /// Creates the required indexes for this repository's collection.
    /// This method should be idempotent - safe to call multiple times.
    /// </summary>
    Task EnsureIndexesAsync();
    
    /// <summary>
    /// The name of the MongoDB collection this repository manages.
    /// Used for logging and diagnostics during index creation.
    /// </summary>
    string CollectionName { get; }
}