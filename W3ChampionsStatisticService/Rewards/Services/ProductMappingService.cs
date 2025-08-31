using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Abstractions;
using W3C.Domain.Rewards.Entities;
using W3C.Domain.Common.Repositories;
using W3C.Domain.Rewards.Repositories;

namespace W3ChampionsStatisticService.Rewards.Services;

public class ProductMappingService(
    IProductMappingRepository productMappingRepo,
    IProductMappingUserAssociationRepository associationRepo,
    ILogger<ProductMappingService> logger) : IProductMappingService
{
    private readonly IProductMappingRepository _productMappingRepo = productMappingRepo;
    private readonly IProductMappingUserAssociationRepository _associationRepo = associationRepo;
    private readonly ILogger<ProductMappingService> _logger = logger;

    public async Task<ProductMapping> GetProductMappingById(string id)
    {
        return await _productMappingRepo.GetById(id);
    }

    public async Task<List<ProductMapping>> GetAllProductMappings()
    {
        return await _productMappingRepo.GetAll();
    }

    public async Task<List<ProductMappingUserAssociation>> GetUserAssociationsByUserId(string userId)
    {
        var associations = await _associationRepo.GetProductMappingsByUserId(userId);
        return associations.Where(a => a.IsActive()).ToList();
    }

    public async Task<List<ProductMappingUserAssociation>> GetAssociationsByProductMappingId(string mappingId)
    {
        var associations = await _associationRepo.GetUsersByProductMappingId(mappingId);
        return associations.Where(a => a.IsActive()).ToList();
    }

    public async Task<ProductMappingUserAssociation> GetUserAssociation(string userId, string mappingId)
    {
        var associations = await _associationRepo.GetByUserAndProductMapping(userId, mappingId);
        return associations.FirstOrDefault(a => a.IsActive());
    }
}
