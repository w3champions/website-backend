using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Rewards.Entities;

namespace W3C.Domain.Rewards.Repositories;

/// <summary>
/// Repository interface for managing direct associations between product mappings and users
/// </summary>
public interface IProductMappingUserAssociationRepository
{
    /// <summary>
    /// Get all users associated with a specific product mapping
    /// </summary>
    /// <param name="productMappingId">The product mapping ID</param>
    /// <returns>List of user associations for the product mapping</returns>
    Task<List<ProductMappingUserAssociation>> GetUsersByProductMappingId(string productMappingId);

    /// <summary>
    /// Get all product mappings associated with a specific user
    /// </summary>
    /// <param name="userId">The user ID (BattleTag)</param>
    /// <returns>List of product mapping associations for the user</returns>
    Task<List<ProductMappingUserAssociation>> GetProductMappingsByUserId(string userId);

    /// <summary>
    /// Get a specific association by ID
    /// </summary>
    /// <param name="id">The association ID</param>
    /// <returns>The association, or null if not found</returns>
    Task<ProductMappingUserAssociation> GetById(string id);

    /// <summary>
    /// Get associations for a user and product mapping combination
    /// </summary>
    /// <param name="userId">The user ID (BattleTag)</param>
    /// <param name="productMappingId">The product mapping ID</param>
    /// <returns>List of associations matching the criteria</returns>
    Task<List<ProductMappingUserAssociation>> GetByUserAndProductMapping(string userId, string productMappingId);

    /// <summary>
    /// Get associations by provider and product ID
    /// </summary>
    /// <param name="providerId">The provider ID</param>
    /// <param name="providerProductId">The provider's product ID</param>
    /// <returns>List of associations for this provider product</returns>
    Task<List<ProductMappingUserAssociation>> GetByProviderProduct(string providerId, string providerProductId);

    /// <summary>
    /// Get associations by user and provider product
    /// </summary>
    /// <param name="userId">The user ID (BattleTag)</param>
    /// <param name="providerId">The provider ID</param>
    /// <param name="providerProductId">The provider's product ID</param>
    /// <returns>List of associations matching the criteria</returns>
    Task<List<ProductMappingUserAssociation>> GetByUserAndProviderProduct(string userId, string providerId, string providerProductId);

    /// <summary>
    /// Get all associations, optionally filtered by status
    /// </summary>
    /// <param name="status">Optional status filter</param>
    /// <returns>List of all associations</returns>
    Task<List<ProductMappingUserAssociation>> GetAll(AssociationStatus? status = null);

    /// <summary>
    /// Create a new association
    /// </summary>
    /// <param name="association">The association to create</param>
    /// <returns>The created association</returns>
    Task<ProductMappingUserAssociation> Create(ProductMappingUserAssociation association);

    /// <summary>
    /// Update an existing association
    /// </summary>
    /// <param name="association">The association to update</param>
    /// <returns>The updated association</returns>
    Task<ProductMappingUserAssociation> Update(ProductMappingUserAssociation association);

    /// <summary>
    /// Delete an association by ID
    /// </summary>
    /// <param name="id">The association ID to delete</param>
    Task Delete(string id);

    /// <summary>
    /// Get expired associations as of a specific date
    /// </summary>
    /// <param name="asOf">The date to check expiration against</param>
    /// <returns>List of expired associations</returns>
    Task<List<ProductMappingUserAssociation>> GetExpiredAssociations(DateTime asOf);
}
