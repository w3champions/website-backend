using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Rewards.Entities;

namespace W3C.Domain.Rewards.Repositories;

public interface IProductMappingRepository
{
    Task<ProductMapping> GetById(string id);
    Task<List<ProductMapping>> GetAll();
    Task<ProductMapping> Create(ProductMapping mapping);
    Task<ProductMapping> Update(ProductMapping mapping);
    Task Delete(string id);
    Task<List<ProductMapping>> GetByProviderAndProductId(string providerId, string productId);
    Task<List<ProductMapping>> GetByProviderId(string providerId);
    Task<List<ProductMapping>> GetByRewardId(string rewardId);
}
