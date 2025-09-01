using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Rewards.Entities;

namespace W3C.Domain.Rewards.Abstractions;

public interface IProductMappingService
{
    Task<ProductMapping> GetProductMappingById(string id);
    Task<List<ProductMapping>> GetAllProductMappings();
    Task<List<ProductMappingUserAssociation>> GetUserAssociationsByUserId(string userId);
    Task<List<ProductMappingUserAssociation>> GetAssociationsByProductMappingId(string mappingId);
    Task<ProductMappingUserAssociation> GetUserAssociation(string userId, string mappingId);
}
