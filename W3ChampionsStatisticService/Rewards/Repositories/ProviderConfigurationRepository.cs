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
public class ProviderConfigurationRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IProviderConfigurationRepository
{
    public Task<ProviderConfiguration> GetByProviderId(string providerId)
    {
        return LoadFirst<ProviderConfiguration>(c => c.ProviderId == providerId);
    }

    public Task<List<ProviderConfiguration>> GetAll()
    {
        return LoadAll<ProviderConfiguration>();
    }


    public async Task<ProviderConfiguration> Create(ProviderConfiguration configuration)
    {
        await Insert(configuration);
        return configuration;
    }

    public async Task<ProviderConfiguration> Update(ProviderConfiguration configuration)
    {
        await Upsert(configuration, c => c.ProviderId == configuration.ProviderId);
        return configuration;
    }

    public Task Delete(string providerId)
    {
        return Delete<ProviderConfiguration>(c => c.ProviderId == providerId);
    }

    public async Task<ProductMapping> GetProductMapping(string providerId, string providerProductId)
    {
        var config = await GetByProviderId(providerId);
        return config?.ProductMappings?.FirstOrDefault(m => m.ProviderProductId == providerProductId);
    }
}