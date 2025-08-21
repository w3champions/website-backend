using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Rewards.Repositories;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Rewards.Repositories;

[Trace]
public class ProductMappingUserAssociationRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IProductMappingUserAssociationRepository
{
    public Task<List<ProductMappingUserAssociation>> GetUsersByProductMappingId(string productMappingId)
    {
        return LoadAll<ProductMappingUserAssociation>(a => a.ProductMappingId == productMappingId);
    }

    public Task<List<ProductMappingUserAssociation>> GetProductMappingsByUserId(string userId)
    {
        return LoadAll<ProductMappingUserAssociation>(a => a.UserId == userId);
    }

    public Task<ProductMappingUserAssociation> GetById(string id)
    {
        return LoadFirst<ProductMappingUserAssociation>(a => a.Id == id);
    }

    public Task<List<ProductMappingUserAssociation>> GetByUserAndProductMapping(string userId, string productMappingId)
    {
        return LoadAll<ProductMappingUserAssociation>(a => 
            a.UserId == userId && a.ProductMappingId == productMappingId);
    }

    public Task<List<ProductMappingUserAssociation>> GetByProviderProduct(string providerId, string providerProductId)
    {
        return LoadAll<ProductMappingUserAssociation>(a => 
            a.ProviderId == providerId && a.ProviderProductId == providerProductId);
    }

    public Task<List<ProductMappingUserAssociation>> GetByUserAndProviderProduct(string userId, string providerId, string providerProductId)
    {
        return LoadAll<ProductMappingUserAssociation>(a => 
            a.UserId == userId && 
            a.ProviderId == providerId && 
            a.ProviderProductId == providerProductId);
    }

    public Task<List<ProductMappingUserAssociation>> GetAll(AssociationStatus? status = null)
    {
        if (status.HasValue)
        {
            return LoadAll<ProductMappingUserAssociation>(a => a.Status == status.Value);
        }
        return LoadAll<ProductMappingUserAssociation>();
    }

    public async Task<ProductMappingUserAssociation> Create(ProductMappingUserAssociation association)
    {
        await Insert(association);
        return association;
    }

    public async Task<ProductMappingUserAssociation> Update(ProductMappingUserAssociation association)
    {
        await Upsert(association, a => a.Id == association.Id);
        return association;
    }

    public Task Delete(string id)
    {
        return Delete<ProductMappingUserAssociation>(a => a.Id == id);
    }

    public Task<List<ProductMappingUserAssociation>> GetExpiredAssociations(DateTime asOf)
    {
        return LoadAll<ProductMappingUserAssociation>(a => 
            a.Status == AssociationStatus.Active && 
            a.ExpiresAt != null && 
            a.ExpiresAt <= asOf);
    }
}