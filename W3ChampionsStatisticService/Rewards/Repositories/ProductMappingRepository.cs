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
public class ProductMappingRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IProductMappingRepository
{
    public Task<List<ProductMapping>> GetByProviderAndProductId(string providerId, string productId)
    {
        return LoadAll<ProductMapping>(m => m.ProductProviders.Any(pp => pp.ProviderId == providerId && pp.ProductId == productId));
    }

    public Task<List<ProductMapping>> GetByProviderId(string providerId)
    {
        return LoadAll<ProductMapping>(m => m.ProductProviders.Any(pp => pp.ProviderId == providerId));
    }

    public Task<List<ProductMapping>> GetByRewardId(string rewardId)
    {
        return LoadAll<ProductMapping>(m => m.RewardIds.Contains(rewardId));
    }

    public Task<List<ProductMapping>> GetAll()
    {
        return LoadAll<ProductMapping>();
    }

    public Task<ProductMapping> GetById(string id)
    {
        return LoadFirst<ProductMapping>(m => m.Id == id);
    }

    public async Task<ProductMapping> Create(ProductMapping mapping)
    {
        await Insert(mapping);
        return mapping;
    }

    public async Task<ProductMapping> Update(ProductMapping mapping)
    {
        await Upsert(mapping, m => m.Id == mapping.Id);
        return mapping;
    }

    public Task Delete(string id)
    {
        return Delete<ProductMapping>(m => m.Id == id);
    }
}