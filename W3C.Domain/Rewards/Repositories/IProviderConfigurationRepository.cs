using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Rewards.Entities;

namespace W3C.Domain.Rewards.Repositories;

public interface IProviderConfigurationRepository
{
    Task<ProviderConfiguration> GetByProviderId(string providerId);
    Task<List<ProviderConfiguration>> GetAll();
    Task<ProviderConfiguration> Create(ProviderConfiguration configuration);
    Task<ProviderConfiguration> Update(ProviderConfiguration configuration);
    Task Delete(string providerId);
    Task<ProductMapping> GetProductMapping(string providerId, string providerProductId);
    Task<ProductMapping> GetProductMappingById(string providerId, string mappingId);
}